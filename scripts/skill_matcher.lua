Log('INFO', '=== SkillMatcher Lua Engine Loading ===')

-- 상수 및 열거형 정의
local SkillState = {
    Casting = 0,
    Targeting = 1,
    Ending = 2,
    Hit = 3,
    Idle = 4,
    Instant = 5
}

local SkillType = {
    Casting = 0,
    TargetCasting = 1,
    Channeling = 2,
    Instant = 3,
    Dot = 4,
    Installation = 5
}

-- 설정 상수
local SKILL_TIMEOUT_SECONDS = 10.0
local INSTALLATION_TIMEOUT_SECONDS = 30.0

-- 전역 데이터 저장소
local castingSkills = {}              -- string -> ActiveSkillInfo
local activeSkills = {}               -- string -> table[datetime -> ActiveSkillInfo]
local installationsByOwner = {}       -- string -> InstallationInfo[]
local installationsByTarget = {}      -- string -> InstallationInfo[]
local pendingDamages = {}             -- queue
local pendingHpChanges = {}           -- queue for HP change packets
local pendingSkillStates = {}         -- queue for SkillState packets (10299)
local pendingChangeHp = {}            -- queue for ChangeHP packets (100178)
local pendingSkillStates = {}          -- queue for SkillState packets
local pendingChangeHp = {}            -- queue for ChangeHp packets

-- ActiveSkillInfo 생성 함수
local function createActiveSkillInfo(usedBy, originalTarget, currentTarget, nextTarget, 
                                   skillName, state, skillType, startTime, lastStateTime, 
                                   isUsing, targetingCount)
    return {
        UsedBy = usedBy,
        OriginalTarget = originalTarget,
        CurrentTarget = currentTarget,
        NextTarget = nextTarget,
        SkillName = skillName,
        State = state,
        Type = skillType,
        StartTime = startTime,
        LastStateTime = lastStateTime,
        IsUsing = isUsing or false,
        TargetingCount = targetingCount or 0,
        TargetHistory = {currentTarget},
        StateHistory = {[state] = startTime}
    }
end

-- InstallationInfo 생성 함수
local function createInstallationInfo(installationId, owner, target, skillName, registeredAt)
    return {
        InstallationId = installationId,
        Owner = owner,
        Target = target,
        SkillName = skillName,
        RegisteredAt = registeredAt
    }
end

-- 유틸리티 함수들
local function getTimeDiffMs(time1, time2)
    return math.abs((time1 - time2) * 1000)
end

local function tableContains(table, value)
    for _, v in pairs(table) do
        if v == value then
            return true
        end
    end
    return false
end

local function addToTargetHistory(targetHistory, newTarget)
    if not tableContains(targetHistory, newTarget) then
        table.insert(targetHistory, newTarget)
    end
end

-- 타겟 매칭 함수
local function isTargetMatch(skillTarget, skillNextTarget, damageTarget)
    -- 정확히 일치하면 매칭
    if skillTarget == damageTarget or skillNextTarget == damageTarget then
        return true
    end
    
    -- 둘 중 하나가 특수 값이면 매칭 (광역/전체 공격)
    local specialValues = {"00000000", "ffffffff"}
    for _, special in pairs(specialValues) do
        if skillTarget == special or damageTarget == special or skillNextTarget == special then
            return true
        end
    end
    
    return false
end

-- 기존 isTargetMatch
local function isTargetMatchSimple(skillTarget, damageTarget)
    return isTargetMatch(skillTarget, "00000000", damageTarget)
end

-- 스킬명 파싱
local function parseSkillName(actionName)
    local baseName, state, skillType
    
    if string.match(actionName, "_Casting$") then
        baseName = string.sub(actionName, 1, -9)
        state = SkillState.Casting
        skillType = SkillType.Casting
    elseif string.match(actionName, "_Targeting$") then
        baseName = string.sub(actionName, 1, -11)
        state = SkillState.Targeting
        skillType = SkillType.TargetCasting
    elseif string.match(actionName, "_End$") then
        baseName = string.sub(actionName, 1, -5)
        state = SkillState.Ending
        skillType = findExistingSkillType(baseName)
    elseif string.match(actionName, "_Hit$") then
        baseName = string.sub(actionName, 1, -5)
        state = SkillState.Hit
        skillType = findExistingSkillType(baseName)
    elseif actionName == "Idle" then
        baseName = actionName
        state = SkillState.Idle
        skillType = SkillType.Instant
    else
        -- 기본값: 즉시 스킬
        baseName = actionName
        state = SkillState.Instant
        skillType = SkillType.Instant
    end
    
    return baseName, state, skillType
end

-- 기존 스킬 타입 찾기
function findExistingSkillType(baseSkillName)
    local latestTime = 0
    local foundType = SkillType.Casting
    
    for _, skill in pairs(castingSkills) do
        if skill.SkillName == baseSkillName and skill.LastStateTime > latestTime then
            latestTime = skill.LastStateTime
            foundType = skill.Type
        end
    end
    
    return foundType
end

-- 스킬 키 생성
local function getSkillKey(usedBy, startTime)
    return usedBy .. "_" .. tostring(startTime)
end

-- 활성 스킬 키 찾기
local function findActiveSkillKey(usedBy, skillName)
    local bestKey = nil
    local latestTime = 0
    
    for key, skill in pairs(castingSkills) do
        if skill.UsedBy == usedBy and skill.SkillName == skillName and skill.State ~= SkillState.Idle then
            if skill.LastStateTime > latestTime then
                latestTime = skill.LastStateTime
                bestKey = key
            end
        end
    end
    
    return bestKey
end

-- 채널링 가능성 판단
local function canBeChannelingSkill(skill, damageTime)
    -- 1. NextTarget이 설정된 스킬 특성 분석
    local hasDistinctNextTarget = skill.NextTarget and 
                                  skill.NextTarget ~= "00000000" and 
                                  skill.NextTarget ~= skill.CurrentTarget

    -- 2. 캐스팅 관련 시간 계산
    local castingDuration = 0
    local damageDelay = 0
    
    if skill.State == SkillState.Ending then
        castingDuration = (skill.LastStateTime - skill.StartTime) * 1000  -- ms 변환
        damageDelay = (damageTime - skill.LastStateTime) * 1000
    else
        castingDuration = (damageTime - skill.StartTime) * 1000
    end
    
    Log('DEBUG', string.format('[Lua] Analyzing skill: %s State: %s CastingDuration: %.0fms DamageDelay: %.0fms', 
        skill.SkillName, skill.State, castingDuration, damageDelay))
    
    -- 3. Lightning 패턴: 적당한 캐스팅 + 긴 지연 = 즉시 스킬
    if skill.State == SkillState.Ending then
        -- 캐스팅 시간이 적당하고(0.4~1.5초), 데미지 지연이 긴 경우(0.5초 이상)
        if castingDuration >= 400 and castingDuration <= 1500 and damageDelay >= 500 then
            Log('DEBUG', string.format('[Lua] Lightning pattern detected: %s (Medium casting + Long delay)', skill.SkillName))
            return false -- 채널링 불가 (즉시 스킬)
        end
        
        -- 텔레키네시스 패턴: 짧은 캐스팅 + 즉시 데미지 = 채널링 가능
        if castingDuration <= 800 and damageDelay <= 300 then
            Log('DEBUG', string.format('[Lua] Telekinesis pattern detected: %s (Short casting + Quick damage)', skill.SkillName))
            return true -- 채널링 가능
        end
    end
    
    -- 4. Casting 중 즉시 데미지 = 채널링 가능
    if skill.State == SkillState.Casting then
        local castingToDamageDelay = (damageTime - skill.LastStateTime) * 1000
        
        -- 캐스팅 중 또는 캐스팅 직후 즉시 데미지(500ms 이내)
        if castingToDamageDelay <= 500 then
            Log('DEBUG', string.format('[Lua] Quick channeling pattern detected: %s CastingToDamageDelay: %.0fms', 
                skill.SkillName, castingToDamageDelay))
            return true -- 채널링 가능
        end
    end
    
    -- 5. NextTarget 기반 추가 분석 (타겟팅 스킬 특성)
    if hasDistinctNextTarget then
        -- NextTarget이 다르면 기본적으로 타겟팅 스킬로 간주
        -- 하지만 시간 패턴이 채널링을 나타내면 예외 허용
        if skill.State == SkillState.Ending and castingDuration <= 800 and damageDelay <= 300 then
            Log('DEBUG', string.format('[Lua] NextTarget skill with channeling pattern: %s NextTarget: %s but allowing channeling', 
                skill.SkillName, skill.NextTarget))
            return true -- 시간 패턴 우선
        end
        
        Log('DEBUG', string.format('[Lua] NextTarget skill treated as targeting: %s CurrentTarget: %s NextTarget: %s', 
            skill.SkillName, skill.CurrentTarget, skill.NextTarget))
        return false -- 기본적으로 채널링 불가
    end
    
    -- 6. 기본값: 채널링 불가 (안전한 기본값)
    Log('DEBUG', string.format('[Lua] Default to non-channeling: %s (No clear pattern detected)', skill.SkillName))
    return false
end

-- 설치물 스킬인지 판단
local function isInstallationSkill(skillInfo)
    -- 1. 기본 조건 체크
    if skillInfo.Owner == "00000000" or not skillInfo.Target or string.len(skillInfo.UsedBy) ~= 8 then
        return false
    end
    
    -- 2. 자기 대상 버프 제외: 자기 자신 대상 (마나회복 등)
    if skillInfo.UsedBy == skillInfo.Owner and skillInfo.Target == skillInfo.Owner then
        Log('DEBUG', string.format('[Lua] Skipped self-buff skill: %s Self-target: %s', 
            skillInfo.SkillName, skillInfo.Owner))
        return false
    end
    
    -- 3. Hit 패턴 특별 처리 (중요!)
    local baseName, state, skillType = parseSkillName(skillInfo.SkillName)
    if state == SkillState.Hit then
        -- Hit 상태의 설치물은 항상 허용 (채널링 관련 체크 건너뛰기)
        local isInstallationHit = skillInfo.UsedBy ~= skillInfo.Owner and skillInfo.Target ~= skillInfo.Owner
        
        if isInstallationHit then
            Log('DEBUG', string.format('[Lua] Detected installation Hit: %s InstallationId: %s Owner: %s Target: %s', 
                skillInfo.SkillName, skillInfo.UsedBy, skillInfo.Owner, skillInfo.Target))
            return true -- Hit 설치물은 무조건 등록
        end
        
        -- Hit_Lightning 등 자기 준비 신호는 설치물이 아님
        if skillInfo.UsedBy == skillInfo.Owner then
            Log('DEBUG', string.format('[Lua] Skipped self Hit signal: %s UsedBy: %s Owner: %s', 
                skillInfo.SkillName, skillInfo.UsedBy, skillInfo.Owner))
            return false
        end
    end
    
    -- 4. 일반 설치물 패턴
    local isRealInstallation = skillInfo.UsedBy ~= skillInfo.Owner and skillInfo.Target ~= skillInfo.Owner
    
    if isRealInstallation then
        Log('DEBUG', string.format('[Lua] Detected real installation: %s InstallationId: %s Owner: %s Target: %s', 
            skillInfo.SkillName, skillInfo.UsedBy, skillInfo.Owner, skillInfo.Target))
    end
    
    return isRealInstallation
end

-- 설치물 등록
local function registerInstallation(skillInfo, lastAt)
    local installation = createInstallationInfo(
        skillInfo.UsedBy,
        skillInfo.Owner,
        skillInfo.Target,
        skillInfo.SkillName,
        lastAt
    )
    
    -- Owner별 인덱스
    if not installationsByOwner[skillInfo.Owner] then
        installationsByOwner[skillInfo.Owner] = {}
    end
    table.insert(installationsByOwner[skillInfo.Owner], installation)
    
    -- Target별 인덱스
    if not installationsByTarget[skillInfo.Target] then
        installationsByTarget[skillInfo.Target] = {}
    end
    table.insert(installationsByTarget[skillInfo.Target], installation)
    
    Log('INFO', string.format('[Lua] Registered installation: %s InstallationId: %s Owner: %s Target: %s', 
        skillInfo.SkillName, skillInfo.UsedBy, skillInfo.Owner, skillInfo.Target))
end

-- 스킬 상태 업데이트
local function updateSkillState(skillKey, packet, newState, receivedAt)
    local existingSkill = castingSkills[skillKey]
    if not existingSkill then
        Log('WARN', string.format('[Lua] Skill not found for update: %s', skillKey))
        return
    end
    
    -- 상태 히스토리 업데이트
    existingSkill.StateHistory[newState] = receivedAt
    existingSkill.State = newState
    existingSkill.LastStateTime = receivedAt
    
    -- 타겟 정보 업데이트 (패킷에 타겟 정보가 있는 경우)
    if packet.Target and packet.Target ~= existingSkill.CurrentTarget then
        addToTargetHistory(existingSkill.TargetHistory, packet.Target)
        existingSkill.CurrentTarget = packet.Target
    end
    
    -- 타겟팅 카운트 업데이트 (필요한 경우)
    if newState == SkillState.Targeting then
        existingSkill.TargetingCount = existingSkill.TargetingCount + 1
    end
    
    castingSkills[skillKey] = existingSkill
    
    Log('DEBUG', string.format('[Lua] Updated skill state: %s → %s (Key: %s)', 
        existingSkill.SkillName, newState, skillKey))
end

-- 캐스팅 스킬 매칭 시도 (Legacy 로직 완전 이전)
local function tryMatchCastingSkill(damagePacket, lastAttackTime)
    local damageTarget = damagePacket.Target
    
    Log('DEBUG', string.format('[Lua] Searching for casting skill candidates with UsedBy: %s, Target: %s', 
        damagePacket.UsedBy, damageTarget))
    
    -- 1. 채널링 패턴: Casting 상태에서 Damage가 들어오면 Channeling으로 전이
    local channelingCandidate = nil
    local bestChannelingDiff = math.huge
    
    for key, skill in pairs(castingSkills) do
        if skill.UsedBy == damagePacket.UsedBy and
           skill.Type == SkillType.Casting and
           skill.State == SkillState.Casting and
           not skill.IsUsing and  -- 아직 사용되지 않은 스킬만
           canBeChannelingSkill(skill, lastAttackTime) then  -- 패킷 특성 기반 판단!
           
            local targetMatch = isTargetMatch(skill.CurrentTarget, skill.NextTarget, damageTarget)
            local timeDiff = getTimeDiffMs(lastAttackTime, skill.LastStateTime)
            
            if targetMatch or timeDiff <= 3000 then
                if timeDiff < bestChannelingDiff then
                    bestChannelingDiff = timeDiff
                    channelingCandidate = {key = key, skill = skill}
                end
            end
        end
    end
    
    if channelingCandidate then
        local skill = channelingCandidate.skill
        local key = channelingCandidate.key
        
        -- 채널링으로 전환
        skill.Type = SkillType.Channeling
        skill.CurrentTarget = damageTarget
        skill.IsUsing = true
        skill.LastStateTime = lastAttackTime
        
        -- 타겟 히스토리 업데이트
        addToTargetHistory(skill.TargetHistory, damageTarget)
        
        castingSkills[key] = skill
        
        Log('INFO', string.format('[Lua] Casting skill changed to Channeling: %s UsedBy: %s Target: %s → %s NextTarget: %s TimeDiff: %.0fms',
            skill.SkillName, skill.UsedBy, skill.CurrentTarget, damageTarget, skill.NextTarget, bestChannelingDiff))
        
        return {Key = key, Skill = skill}
    end
    
    -- 2. 즉시 데미지 패턴: Casting/Ending 상태에서 즉시 데미지
    local instantCandidate = nil
    local bestInstantDiff = math.huge
    
    for key, skill in pairs(castingSkills) do
        if skill.UsedBy == damagePacket.UsedBy and
           skill.Type == SkillType.Casting and
           (skill.State == SkillState.Casting or skill.State == SkillState.Ending) and
           not skill.IsUsing and  -- 아직 사용되지 않은 스킬만
           not canBeChannelingSkill(skill, lastAttackTime) and  -- 채널링 불가능한 스킬만!
           isTargetMatch(skill.CurrentTarget, skill.NextTarget, damageTarget) then
           
            local timeDiff = getTimeDiffMs(lastAttackTime, skill.LastStateTime)
            
            if timeDiff <= 2000 then  -- 2초 이내 즉시 데미지
                if timeDiff < bestInstantDiff then
                    bestInstantDiff = timeDiff
                    instantCandidate = {key = key, skill = skill}
                end
            end
    end
    
    if instantCandidate then
        local skill = instantCandidate.skill
        local key = instantCandidate.key
        
        -- 즉시 스킬은 채널링 전환하지 않고 Casting 상태 유지하면서 데미지 매칭
        skill.IsUsing = true
        skill.LastStateTime = lastAttackTime
        
        castingSkills[key] = skill
        
        Log('INFO', string.format('[Lua] Instant casting skill matched: %s UsedBy: %s Target: %s NextTarget: %s State: %s TimeDiff: %.0fms',
            skill.SkillName, skill.UsedBy, damageTarget, skill.NextTarget, skill.State, bestInstantDiff))
        
        return {Key = key, Skill = skill}
    end
    
    -- 3. 타게팅 캐스팅 패턴: TargetCasting + Ending 상태에서만 매칭
    local targetingCandidate = nil
    local bestTargetingDiff = math.huge
    
    for key, skill in pairs(castingSkills) do
        if isTargetMatchSimple(skill.CurrentTarget, damageTarget) and
           skill.UsedBy == damagePacket.UsedBy and
           not skill.IsUsing and
           skill.Type == SkillType.TargetCasting and
           (skill.State == SkillState.Targeting or skill.State == SkillState.Ending) then
           
            local timeDiff = getTimeDiffMs(lastAttackTime, skill.LastStateTime)
            
            if timeDiff < bestTargetingDiff then
                bestTargetingDiff = timeDiff
                targetingCandidate = {key = key, skill = skill}
            end
        end
    end
    
    if targetingCandidate then
        local skill = targetingCandidate.skill
        local key = targetingCandidate.key
        
        skill.IsUsing = true
        skill.LastStateTime = lastAttackTime
        
        castingSkills[key] = skill
        
        Log('INFO', string.format('[Lua] TargetCasting skill matched: %s UsedBy: %s Target: %s State: %s TargetingCount: %d',
            skill.SkillName, skill.UsedBy, skill.CurrentTarget, skill.State, skill.TargetingCount))
        
        return {Key = key, Skill = skill}
    end
    
    -- 4. Lazy Casting 매칭 + 지연 정리
    local lazyCastingCandidates = {}
    
    for key, skill in pairs(castingSkills) do
        if skill.UsedBy == damagePacket.UsedBy and
           skill.Type == SkillType.Casting and
           skill.State == SkillState.Ending and
           not skill.IsUsing then
            table.insert(lazyCastingCandidates, {key = key, skill = skill})
        end
    end
    
    for _, candidate in pairs(lazyCastingCandidates) do
        local skill = candidate.skill
        local key = candidate.key
        local timeDiff = getTimeDiffMs(lastAttackTime, skill.LastStateTime)
        
        -- A. 타겟 매칭되는 스킬이 있다면 매칭 시도
        if isTargetMatch(skill.CurrentTarget, skill.NextTarget, damageTarget) then
            -- Lazy Casting은 더 긴 시간 허용 (지연된 데미지 패턴)
            if timeDiff <= 10000 then  -- 10초 이내 매칭 허용
                skill.IsUsing = true
                skill.LastStateTime = lastAttackTime
                castingSkills[key] = skill
                
                Log('INFO', string.format('[Lua] Lazy Casting skill matched: %s UsedBy: %s Target: %s NextTarget: %s TimeDiff: %.0fms',
                    skill.SkillName, skill.UsedBy, skill.CurrentTarget, skill.NextTarget, timeDiff))
                
                return {Key = key, Skill = skill}
            end
        end
        
        -- B. 광역 공격 패턴 매칭
        if skill.CurrentTarget == "ffffffff" or skill.NextTarget == "ffffffff" then
            -- 광역 스킬은 더 긴 지연 시간 허용
            if timeDiff <= 15000 then  -- 15초 이내 매칭 허용
                skill.IsUsing = true
                skill.LastStateTime = lastAttackTime
                castingSkills[key] = skill
                
                Log('INFO', string.format('[Lua] Area Casting skill matched: %s UsedBy: %s AreaTarget: %s ActualTarget: %s TimeDiff: %.0fms',
                    skill.SkillName, skill.UsedBy, skill.CurrentTarget, damageTarget, timeDiff))
                
                return {Key = key, Skill = skill}
            end
        end
        
        -- C. 매칭되지 않거나 시간 초과된 스킬들 정리 (시간 범위 확장)
        if timeDiff > 15000 then  -- 15초 후 정리
            castingSkills[key] = nil
            Log('INFO', string.format('[Lua] Cleaned up expired lazy casting skill: %s (TimeDiff: %.0fms)', 
                skill.SkillName, timeDiff))
        elseif timeDiff > 3000 then  -- 3초 후부터 대기 상태 로깅
            Log('DEBUG', string.format('[Lua] Lazy casting skill waiting for damage: %s (TimeDiff: %.0fms)', 
                skill.SkillName, timeDiff))
        end
    end
    
    return nil
end

-- 채널링 스킬 매칭 시도
local function tryMatchChannelingSkill(damagePacket, lastAttackTime)
    local bestMatch = nil
    local bestDiff = math.huge
    
    for key, skill in pairs(castingSkills) do
        if skill.Type == SkillType.Channeling and
           skill.UsedBy == damagePacket.UsedBy and
           isTargetMatchSimple(skill.CurrentTarget, damagePacket.Target) then
            
            local timeDiff = getTimeDiffMs(lastAttackTime, skill.LastStateTime)
            
            if timeDiff < bestDiff then
                bestDiff = timeDiff
                bestMatch = {key = key, skill = skill}
            end
        end
    end
    
    if bestMatch then
        local skill = bestMatch.skill
        local key = bestMatch.key
        
        -- 채널링 스킬이 진행 중인 경우 마지막 데미지 시간으로 상태 정보 업데이트
        if skill.State == SkillState.Casting or skill.State == SkillState.Ending then
            skill.LastStateTime = lastAttackTime
            castingSkills[key] = skill
        elseif skill.State == SkillState.Idle then
            -- 채널링 스킬이 Idle 상태인 경우, 채널링이 끝난 것으로 간주하고 제거
            castingSkills[key] = nil
        end
        
        return {Key = key, Skill = skill}
    end
    
    return nil
end

-- 설치물 매칭 시도
local function tryMatchInstallationByData(damagePacket, lastAttackTime)
    local matchedInstallation = nil
    
    -- 1차 매칭: Owner + Target 기준
    local ownerInstallations = installationsByOwner[damagePacket.UsedBy]
    if ownerInstallations then
        for _, inst in pairs(ownerInstallations) do
            if inst.Target == damagePacket.Target and
               getTimeDiffMs(lastAttackTime, inst.RegisteredAt) <= INSTALLATION_TIMEOUT_SECONDS * 1000 then
                if not matchedInstallation or inst.RegisteredAt > matchedInstallation.RegisteredAt then
                    matchedInstallation = inst
                end
            end
        end
    end
    
    -- 2차 매칭: Target 기준으로 확장 검색
    if not matchedInstallation then
        local targetInstallations = installationsByTarget[damagePacket.Target]
        if targetInstallations then
            for _, inst in pairs(targetInstallations) do
                if inst.Owner == damagePacket.UsedBy and
                   getTimeDiffMs(lastAttackTime, inst.RegisteredAt) <= INSTALLATION_TIMEOUT_SECONDS * 1000 then
                    if not matchedInstallation or inst.RegisteredAt > matchedInstallation.RegisteredAt then
                        matchedInstallation = inst
                    end
                end
            end
        end
    end
    
    if matchedInstallation then
        Log('INFO', string.format('[Lua] Installation matched by data: %s InstallationId: %s Owner: %s Target: %s TimeDiff: %.0fms',
            matchedInstallation.SkillName, matchedInstallation.InstallationId, 
            matchedInstallation.Owner, matchedInstallation.Target, 
            getTimeDiffMs(lastAttackTime, matchedInstallation.RegisteredAt)))
        
        -- 가상의 ActiveSkillInfo 생성 (설치물용)
        local skillInfo = createActiveSkillInfo(
            matchedInstallation.Owner,
            matchedInstallation.Target,
            matchedInstallation.Target,
            "00000000",
            matchedInstallation.SkillName,
            SkillState.Instant,
            SkillType.Installation,
            matchedInstallation.RegisteredAt,
            lastAttackTime
        )
        
        local skillKey = matchedInstallation.SkillName .. "_" .. matchedInstallation.InstallationId
        return {Key = skillKey, Skill = skillInfo}
    end
    
    return nil
end

-- 즉시 스킬 매칭 시도
local function tryMatchInstantSkillSequential(damagePacket, lastAttackTime)
    Log('DEBUG', string.format('[Lua] Sequential matching for user %s', damagePacket.UsedBy))
    
    local MAX_INSTANT_SKILL_TIME_DIFF_MS = 2000
    local userSeqTable = activeSkills[damagePacket.UsedBy]
    
    if not userSeqTable then
        return nil
    end
    
    local bestMatch = nil
    local bestDiff = math.huge
    
    for key, skill in pairs(userSeqTable) do
        if isTargetMatchSimple(skill.CurrentTarget, damagePacket.Target) then
            local timeDiff = getTimeDiffMs(lastAttackTime, skill.LastStateTime)
            
            if timeDiff <= MAX_INSTANT_SKILL_TIME_DIFF_MS and timeDiff < bestDiff then
                bestDiff = timeDiff
                bestMatch = {key = key, skill = skill}
            end
        end
    end
    
    if bestMatch then
        local skill = bestMatch.skill
        local key = bestMatch.key
        
        -- 즉시 스킬은 매칭 후 제거
        if skill.CurrentTarget ~= "ffffffff" then
            userSeqTable[key] = nil
        end
        
        return {Key = tostring(key), Skill = skill}
    end
    
    return nil
end

-- =================
-- 메인 진입점 함수들
-- =================

-- 스킬 액션 처리
function EnqueueSkillAction(skillAction, lastAt)
    local baseName, state, skillType = parseSkillName(skillAction.ActionName)
    local skillKey = getSkillKey(skillAction.UsedBy, lastAt)
    
    if state ~= SkillState.Instant then
        local existingSkill = castingSkills[skillKey]
        if existingSkill then
            updateSkillState(skillKey, skillAction, state, lastAt)
        else
            local skillInfo = createActiveSkillInfo(
                skillAction.UsedBy,
                skillAction.Target,
                skillAction.Target,
                skillAction.NextTarget,
                baseName,
                state,
                skillType,
                lastAt,
                lastAt
            )
            castingSkills[skillKey] = skillInfo
            Log('INFO', string.format('[Lua] Started skill: %s Key: %s', baseName, skillKey))
        end
    elseif state == SkillState.Instant then
        -- 즉시 스킬을 activeSkills에 등록
        if not activeSkills[skillAction.UsedBy] then
            activeSkills[skillAction.UsedBy] = {}
        end
        
        local skillInfo = createActiveSkillInfo(
            skillAction.UsedBy,
            skillAction.Target,
            skillAction.Target,
            skillAction.NextTarget,
            baseName,
            state,
            skillType,
            lastAt,
            lastAt
        )
        activeSkills[skillAction.UsedBy][lastAt] = skillInfo
        Log('INFO', string.format('[Lua] Started instant skill: %s Key: %s_%s', 
            baseName, skillAction.UsedBy, tostring(lastAt)))
    end
end

-- 스킬 정보 처리
function EnqueueSkillInfo(skillInfo, lastAt)
    if isInstallationSkill(skillInfo) then
        registerInstallation(skillInfo, lastAt)
        return
    end
    
    local baseSkillName, state, skillType = parseSkillName(skillInfo.SkillName)
    
    if state == SkillState.Idle then
        -- Idle 스킬은 중복 등록하지 않고 기존 스킬을 Idle 상태로 전환
        local existingSkillKey = findActiveSkillKey(skillInfo.UsedBy, baseSkillName)
        if existingSkillKey then
            updateSkillState(existingSkillKey, skillInfo, state, lastAt)
            return
        end
        
        -- 기존 스킬이 없으면 동일 UsedBy의 모든 스킬을 Idle로 전환 시도
        local latestSkill = nil
        local latestKey = nil
        local latestTime = 0
        
        for key, skill in pairs(castingSkills) do
            if skill.UsedBy == skillInfo.UsedBy and skill.State ~= SkillState.Idle and
               skill.LastStateTime > latestTime then
                latestTime = skill.LastStateTime
                latestSkill = skill
                latestKey = key
            end
        end
        
        if latestSkill then
            updateSkillState(latestKey, skillInfo, state, lastAt)
            Log('INFO', string.format('[Lua] Converted latest skill to Idle: %s', latestSkill.SkillName))
            return
        end
        
        -- 정말 처리할 스킬이 없으면 무시
        Log('DEBUG', string.format('[Lua] Ignoring redundant Idle signal from %s', skillInfo.UsedBy))
        return
    end
    
    if state == SkillState.Instant then
        if not activeSkills[skillInfo.UsedBy] then
            activeSkills[skillInfo.UsedBy] = {}
        end
        
        local skillInfoObj = createActiveSkillInfo(
            skillInfo.UsedBy,
            skillInfo.Target,
            skillInfo.Target,
            skillInfo.Target,
            baseSkillName,
            state,
            skillType,
            lastAt,
            lastAt
        )
        
        activeSkills[skillInfo.UsedBy][lastAt] = skillInfoObj
        Log('INFO', string.format('[Lua] Started instant skill (Info): %s Key: %s_%s', 
            baseSkillName, skillInfo.UsedBy, tostring(lastAt)))
        return
    end
    
    local skillKey = findActiveSkillKey(skillInfo.UsedBy, baseSkillName)
    
    if skillKey then
        updateSkillState(skillKey, skillInfo, state, lastAt)
    else
        skillKey = getSkillKey(skillInfo.UsedBy, lastAt)
        local skillInfoObj = createActiveSkillInfo(
            skillInfo.UsedBy,
            skillInfo.Target,
            skillInfo.Target,
            skillInfo.Target,
            baseSkillName,
            state,
            skillType,
            lastAt,
            lastAt
        )
        castingSkills[skillKey] = skillInfoObj
        Log('INFO', string.format('[Lua] Started skill (Info first): %s Key: %s', baseSkillName, skillKey))
    end
end

-- 데미지 처리
function EnqueueDamage(damage, lastAttackTime)
    table.insert(pendingDamages, {damage = damage, time = lastAttackTime})
    
    -- 즉시 처리하지 않고 MatchDamageToSkill에서 처리
    local matched = MatchDamageToSkill(damage, lastAttackTime)
    if matched and matched.SkillName and matched.SkillName ~= "" then
        -- OnDamageMatchedCallback 호출
        if OnDamageMatchedCallback then
            OnDamageMatchedCallback(matched)
        end
    end
end

-- 메인 데미지 매칭 함수
function MatchDamageToSkill(damagePacket, lastAttackTime)
    -- 0. 도트 스킬은 별도로 처리 (IsDot 메서드가 있다면)
    -- if damagePacket:IsDot() then
    --     return damagePacket
    -- end
    
    local damageTarget = damagePacket.Target
    Log('INFO', string.format('[Lua] Matching damage packet: from %s to %s', damagePacket.UsedBy, damageTarget))
    
    -- 1. 캐스팅 스킬 매칭 시도 (최우선)
    local castingMatch = tryMatchCastingSkill(damagePacket, lastAttackTime)
    if castingMatch then
        Log('INFO', string.format('[Lua] Casting skill matched: [%s] %s for damage %d', 
            castingMatch.Key, castingMatch.Skill.SkillName, damagePacket.Damage))
        damagePacket.SkillName = castingMatch.Skill.SkillName
        return damagePacket
    end
    
    -- 2. 채널링 스킬 매칭 시도
    local channelingMatch = tryMatchChannelingSkill(damagePacket, lastAttackTime)
    if channelingMatch then
        Log('INFO', string.format('[Lua] Channeling skill matched: [%s] %s for damage %d', 
            channelingMatch.Key, channelingMatch.Skill.SkillName, damagePacket.Damage))
        damagePacket.SkillName = channelingMatch.Skill.SkillName
        return damagePacket
    end
    
    -- 3. 설치물 스킬 매칭 시도
    local installationMatch = tryMatchInstallationByData(damagePacket, lastAttackTime)
    if installationMatch then
        Log('INFO', string.format('[Lua] Installation skill matched: [%s] %s for damage %d', 
            installationMatch.Key, installationMatch.Skill.SkillName, damagePacket.Damage))
        damagePacket.SkillName = installationMatch.Skill.SkillName
        return damagePacket
    end
    
    -- 4. 활성 스킬 매칭 시도 (기존 활성 스킬에서)
    local instantMatch = tryMatchInstantSkillSequential(damagePacket, lastAttackTime)
    if instantMatch then
        Log('INFO', string.format('[Lua] Instant skill matched: [%s] %s for damage %d', 
            instantMatch.Key, instantMatch.Skill.SkillName, damagePacket.Damage))
        damagePacket.SkillName = instantMatch.Skill.SkillName
        return damagePacket
    end
    
    Log('INFO', string.format('[Lua] No skill match found for damage From %s To %s Damage %d', 
        damagePacket.UsedBy, damagePacket.Target, damagePacket.Damage))
    return damagePacket
end

-- HP 변화 타입 판단
local function determineHpChangeType(hpChange)
    if hpChange < 0 then
        return HP_CHANGE_TYPE.Damage
    elseif hpChange > 0 then
        return HP_CHANGE_TYPE.Heal
    else
        return HP_CHANGE_TYPE.Unknown
    end
end

-- HP 변화 패킷이 힐링 스킬인지 판단
local function isHealingSkill(skillName)
    if not skillName then return false end
    
    local healingKeywords = {
        "heal", "cure", "recovery", "restore", "regeneration", "blessing",
        "힐", "치유", "회복", "재생", "축복", "치료"
    }
    
    local lowerSkillName = string.lower(skillName)
    for _, keyword in pairs(healingKeywords) do
        if string.find(lowerSkillName, keyword) then
            return true
        end
    end
    
    return false
end

-- 오래된 스킬 정리
function CleanupOldSkills(lastAt)
    -- 활성 스킬 정리
    for usedBy, userSkills in pairs(activeSkills) do
        local expired = {}
        for key, skill in pairs(userSkills) do
            if (lastAt - skill.LastStateTime) > SKILL_TIMEOUT_SECONDS then
                table.insert(expired, key)
            end
        end
        for _, key in pairs(expired) do
            userSkills[key] = nil
            Log('DEBUG', string.format('[Lua] Removed expired active skill: %s', tostring(key)))
        end
    end
    
    -- 캐스팅 스킬 정리
    local expiredCastingKeys = {}
    for key, skill in pairs(castingSkills) do
        if (lastAt - skill.LastStateTime) > SKILL_TIMEOUT_SECONDS then
            table.insert(expiredCastingKeys, key)
        end
    end
    for _, key in pairs(expiredCastingKeys) do
        local skill = castingSkills[key]
        castingSkills[key] = nil
        Log('INFO', string.format('[Lua] Removed expired casting skill: %s UsedBy: %s Target: %s', 
            skill.SkillName, skill.UsedBy, skill.CurrentTarget))
    end
    
    -- 설치물 정리 (30초 이상 된 것들)
    for owner, installations in pairs(installationsByOwner) do
        local validInstallations = {}
        for _, inst in pairs(installations) do
            if (lastAt - inst.RegisteredAt) <= INSTALLATION_TIMEOUT_SECONDS then
                table.insert(validInstallations, inst)
            else
                Log('DEBUG', string.format('[Lua] Removed expired installation: %s', inst.SkillName))
            end
        end
        installationsByOwner[owner] = validInstallations
    end
    
    for target, installations in pairs(installationsByTarget) do
        local validInstallations = {}
        for _, inst in pairs(installations) do
            if (lastAt - inst.RegisteredAt) <= INSTALLATION_TIMEOUT_SECONDS then
                table.insert(validInstallations, inst)
            end
        end
        installationsByTarget[target] = validInstallations
    end
    
end

-- HP 변화 스킬 매칭 시도
local function tryMatchHpChangeSkill(hpChangePacket, lastTime)
    local changeAmount = hpChangePacket.HpChange
    local changeType = determineHpChangeType(changeAmount)
    local target = hpChangePacket.Target
    local caster = hpChangePacket.UsedBy or hpChangePacket.Caster
    
    Log('DEBUG', string.format('[Lua] Matching HP change: Amount: %d Type: %s Caster: %s Target: %s', 
        changeAmount, changeType, caster or "Unknown", target))
    
    -- 1. 캐스팅 스킬에서 힐링/데미지 매칭
    local bestMatch = nil
    local bestDiff = math.huge
    
    for key, skill in pairs(castingSkills) do
        if skill.UsedBy == caster and not skill.IsUsing then
            -- 힐링 스킬과 HP 증가 매칭
            if changeType == HP_CHANGE_TYPE.Heal and isHealingSkill(skill.SkillName) then
                if isTargetMatch(skill.CurrentTarget, skill.NextTarget, target) then
                    local timeDiff = getTimeDiffMs(lastTime, skill.LastStateTime)
                    if timeDiff <= 3000 and timeDiff < bestDiff then
                        bestDiff = timeDiff
                        bestMatch = {key = key, skill = skill, matchType = "Healing"}
                    end
                end
            end
            -- 데미지 스킬과 HP 감소 매칭 (기존 데미지 매칭과 유사)
            elseif changeType == HP_CHANGE_TYPE.Damage and not isHealingSkill(skill.SkillName) then
                if isTargetMatch(skill.CurrentTarget, skill.NextTarget, target) then
                    local timeDiff = getTimeDiffMs(lastTime, skill.LastStateTime)
                    if timeDiff <= 2000 and timeDiff < bestDiff then
                        bestDiff = timeDiff
                        bestMatch = {key = key, skill = skill, matchType = "Damage"}
                    end
                end
            end
        end
    end
    
    if bestMatch then
        local skill = bestMatch.skill
        local key = bestMatch.key
        
        skill.IsUsing = true
        skill.LastStateTime = lastTime
        castingSkills[key] = skill
        
        Log('INFO', string.format('[Lua] HP change skill matched: %s (%s) Caster: %s Target: %s Amount: %d TimeDiff: %.0fms',
            skill.SkillName, bestMatch.matchType, skill.UsedBy, target, changeAmount, bestDiff))
        
        return {Key = key, Skill = skill, ChangeType = changeType}
    end
    
    -- 2. 즉시 스킬에서 매칭 시도
    if caster and activeSkills[caster] then
        for key, skill in pairs(activeSkills[caster]) do
            local isHealMatch = changeType == HP_CHANGE_TYPE.Heal and isHealingSkill(skill.SkillName)
            local isDamageMatch = changeType == HP_CHANGE_TYPE.Damage and not isHealingSkill(skill.SkillName)
            
            if (isHealMatch or isDamageMatch) and isTargetMatchSimple(skill.CurrentTarget, target) then
                local timeDiff = getTimeDiffMs(lastTime, skill.LastStateTime)
                
                if timeDiff <= 2000 then
                    -- 즉시 스킬은 사용 후 제거
                    activeSkills[caster][key] = nil
                    
                    Log('INFO', string.format('[Lua] Instant HP change skill matched: %s Caster: %s Target: %s Amount: %d',
                        skill.SkillName, skill.UsedBy, target, changeAmount))
                    
                    return {Key = tostring(key), Skill = skill, ChangeType = changeType}
                end
            end
        end
    end
    
    -- 3. 설치물에서 힐링/데미지 매칭 시도
    local installationMatch = nil
    
    -- Owner별 설치물 검색
    if caster and installationsByOwner[caster] then
        for _, inst in pairs(installationsByOwner[caster]) do
            if inst.Target == target then
                local timeDiff = getTimeDiffMs(lastTime, inst.RegisteredAt)
                if timeDiff <= INSTALLATION_TIMEOUT_SECONDS * 1000 then
                    local isHealMatch = changeType == HP_CHANGE_TYPE.Heal and isHealingSkill(inst.SkillName)
                    local isDamageMatch = changeType == HP_CHANGE_TYPE.Damage and not isHealingSkill(inst.SkillName)
                    
                    if isHealMatch or isDamageMatch then
                        installationMatch = inst
                        break
                    end
                end
            end
        end
    end
    
    if installationMatch then
        Log('INFO', string.format('[Lua] Installation HP change matched: %s Owner: %s Target: %s Amount: %d',
            installationMatch.SkillName, installationMatch.Owner, installationMatch.Target, changeAmount))
        
        -- 가상의 ActiveSkillInfo 생성 (설치물용)
        local skillInfo = createActiveSkillInfo(
            installationMatch.Owner,
            installationMatch.Target,
            installationMatch.Target,
            "00000000",
            installationMatch.SkillName,
            SkillState.Instant,
            SkillType.Installation,
            installationMatch.RegisteredAt,
            lastTime
        )
        
        local skillKey = installationMatch.SkillName .. "_" .. installationMatch.InstallationId
        return {Key = skillKey, Skill = skillInfo, ChangeType = changeType}
    end
    
    return nil
end

-- HP 변화 패킷 처리
function EnqueueHpChange(hpChangePacket, lastTime)
    table.insert(pendingHpChanges, {hpChange = hpChangePacket, time = lastTime})
    
    -- 즉시 처리
    local matched = MatchHpChangeToSkill(hpChangePacket, lastTime)
    if matched and matched.SkillName and matched.SkillName ~= "" then
        -- OnHpChangeMatchedCallback 호출 (있다면)
        if OnHpChangeMatchedCallback then
            OnHpChangeMatchedCallback(matched)
        elseif OnDamageMatchedCallback then
            -- HP 변화를 데미지 형태로 변환하여 기존 콜백 사용
            local convertedPacket = {
                UsedBy = matched.UsedBy or hpChangePacket.UsedBy or hpChangePacket.Caster,
                Target = matched.Target or hpChangePacket.Target,
                Damage = math.abs(hpChangePacket.HpChange or 0),
                SkillName = matched.SkillName,
                IsHeal = matched.ChangeType == HP_CHANGE_TYPE.Heal
            }
            OnDamageMatchedCallback(convertedPacket)
        end
    end
end

-- HP 변화를 스킬에 매칭
function MatchHpChangeToSkill(hpChangePacket, lastTime)
    local changeAmount = hpChangePacket.HpChange or 0
    local changeType = determineHpChangeType(changeAmount)
    local target = hpChangePacket.Target
    local caster = hpChangePacket.UsedBy or hpChangePacket.Caster
    
    Log('INFO', string.format('[Lua] Matching HP change packet: Amount: %d Type: %s From: %s To: %s', 
        changeAmount, changeType == HP_CHANGE_TYPE.Heal and "Heal" or 
                      changeType == HP_CHANGE_TYPE.Damage and "Damage" or "Unknown", 
        caster or "Unknown", target))
    
    -- HP 변화가 0이면 무시
    if changeAmount == 0 then
        Log('DEBUG', '[Lua] Ignoring zero HP change')
        return hpChangePacket
    end
    
    -- HP 변화 스킬 매칭 시도
    local hpChangeMatch = tryMatchHpChangeSkill(hpChangePacket, lastTime)
    if hpChangeMatch then
        Log('INFO', string.format('[Lua] HP change skill matched: [%s] %s for change %d (%s)', 
            hpChangeMatch.Key, hpChangeMatch.Skill.SkillName, changeAmount,
            hpChangeMatch.ChangeType == HP_CHANGE_TYPE.Heal and "Heal" or "Damage"))
        
        -- 결과 패킷에 스킬명 설정
        hpChangePacket.SkillName = hpChangeMatch.Skill.SkillName
        hpChangePacket.UsedBy = hpChangeMatch.Skill.UsedBy
        return hpChangePacket
    end
    
    Log('INFO', string.format('[Lua] No skill match found for HP change Amount: %d From: %s To: %s', 
        changeAmount, caster or "Unknown", target))
    return hpChangePacket
end

-- SkillStatePacket(10299) 처리
function EnqueueSkillState(skillStatePacket, lastAt)
    Log('INFO', string.format('[Lua] Received SkillState packet: UsedBy: %s Target: %s Action: %s', 
        skillStatePacket.UsageBy or "Unknown", skillStatePacket.Target or "Unknown", 
        skillStatePacket.Action or "Unknown"))
    
    -- SkillState 패킷을 pendingSkillStates에 저장
    if not pendingSkillStates then
        pendingSkillStates = {}
    end
    
    table.insert(pendingSkillStates, {
        packet = skillStatePacket,
        time = lastAt
    })
    
    -- 필요시 즉시 처리 로직 추가 가능
    -- 현재는 저장만 하고 나중에 HP 변화와 조합하여 처리
end

-- ChangeHpPacket(100178) 처리  
function EnqueueChangeHp(changeHpPacket, lastAt)
    local hpChange = (changeHpPacket.CurrentHp or 0) - (changeHpPacket.PrevHp or 0)
    
    Log('INFO', string.format('[Lua] Received ChangeHP packet: Target: %s PrevHP: %d CurrentHP: %d Change: %d', 
        changeHpPacket.Target or "Unknown", changeHpPacket.PrevHp or 0, 
        changeHpPacket.CurrentHp or 0, hpChange))
    
    -- HP 변화가 0이면 무시
    if hpChange == 0 then
        Log('DEBUG', '[Lua] Ignoring zero HP change')
        return
    end
    
    -- pendingChangeHp에 저장
    if not pendingChangeHp then
        pendingChangeHp = {}
    end
    
    table.insert(pendingChangeHp, {
        target = changeHpPacket.Target,
        hpChange = hpChange,
        prevHp = changeHpPacket.PrevHp or 0,
        currentHp = changeHpPacket.CurrentHp or 0,
        time = lastAt
    })
    
    -- 기존 HP 변화 처리 함수와 연동
    local hpChangeData = {
        Target = changeHpPacket.Target,
        HpChange = hpChange,
        PrevHp = changeHpPacket.PrevHp or 0,
        CurrentHp = changeHpPacket.CurrentHp or 0
    }
    
    -- 기존 EnqueueHpChange 함수 호출
    EnqueueHpChange(hpChangeData, lastAt)
end