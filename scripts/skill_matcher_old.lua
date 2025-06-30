-- =============================================================================
-- SkillMatcher 실제 구현 파일
-- =============================================================================
-- 이 파일은 완전한 매칭 알고리즘이 구현되어 있는 실제 사용 가능한 파일입니다.
-- 프레임워크로부터 모든 기본 기능을 제공받으며, 매칭 알고리즘만 구현합니다.
-- =============================================================================

-- 프레임워크 로드 (안전한 경로 처리)
local function loadFramework()
    local possiblePaths = {
        "scripts/skill_matcher_framework.lua",
        "../../../scripts/skill_matcher_framework.lua",
        "skill_matcher_framework.lua"
    }
    
    for _, path in ipairs(possiblePaths) do
        local file = io.open(path, "r")
        if file then
            file:close()
            dofile(path)
            print("[INFO] Framework loaded from: " .. path)
            return true
        end
    end
    
    print("[ERROR] Could not find skill_matcher_framework.lua")
    return false
end

if not loadFramework() then
    print("[FATAL] SkillMatcher framework could not be loaded - system disabled")
    return
end

print("[INFO] SkillMatcher Full Implementation Loaded")

-- =============================================================================
-- 채널링 가능성 판단
-- =============================================================================

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
    
    Log('DEBUG', string.format('[Matching] Analyzing skill: %s State: %s CastingDuration: %.0fms DamageDelay: %.0fms', 
        skill.SkillName, skill.State, castingDuration, damageDelay))
    
    -- 3. Lightning 패턴: 적당한 캐스팅 + 긴 지연 = 즉시 스킬
    if skill.State == SkillState.Ending then
        -- 캐스팅 시간이 적당하고(0.4~1.5초), 데미지 지연이 긴 경우(0.5초 이상)
        if castingDuration >= 400 and castingDuration <= 1500 and damageDelay >= 500 then
            Log('DEBUG', string.format('[Matching] Lightning pattern detected: %s (Medium casting + Long delay)', skill.SkillName))
            return false -- 채널링 불가 (즉시 스킬)
        end
        
        -- 텔레키네시스 패턴: 짧은 캐스팅 + 즉시 데미지 = 채널링 가능
        if castingDuration <= 800 and damageDelay <= 300 then
            Log('DEBUG', string.format('[Matching] Telekinesis pattern detected: %s (Short casting + Quick damage)', skill.SkillName))
            return true -- 채널링 가능
        end
    end
    
    -- 4. Casting 중 즉시 데미지 = 채널링 가능
    if skill.State == SkillState.Casting then
        local castingToDamageDelay = (damageTime - skill.LastStateTime) * 1000
        
        -- 캐스팅 중 또는 캐스팅 직후 즉시 데미지(500ms 이내)
        if castingToDamageDelay <= 500 then
            Log('DEBUG', string.format('[Matching] Quick channeling pattern detected: %s CastingToDamageDelay: %.0fms', 
                skill.SkillName, castingToDamageDelay))
            return true -- 채널링 가능
        end
    end
    
    -- 5. NextTarget 기반 추가 분석 (타겟팅 스킬 특성)
    if hasDistinctNextTarget then
        -- NextTarget이 다르면 기본적으로 타겟팅 스킬로 간주
        -- 하지만 시간 패턴이 채널링을 나타내면 예외 허용
        if skill.State == SkillState.Ending and castingDuration <= 800 and damageDelay <= 300 then
            Log('DEBUG', string.format('[Matching] NextTarget skill with channeling pattern: %s NextTarget: %s but allowing channeling', 
                skill.SkillName, skill.NextTarget))
            return true -- 시간 패턴 우선
        end
        
        Log('DEBUG', string.format('[Matching] NextTarget skill treated as targeting: %s CurrentTarget: %s NextTarget: %s', 
            skill.SkillName, skill.CurrentTarget, skill.NextTarget))
        return false -- 기본적으로 채널링 불가
    end
    
    -- 6. 기본값: 채널링 불가 (안전한 기본값)
    Log('DEBUG', string.format('[Matching] Default to non-channeling: %s (No clear pattern detected)', skill.SkillName))
    return false
end

-- =============================================================================
-- 매칭 알고리즘 구현
-- =============================================================================

-- 1. 캐스팅 스킬 매칭 시도
local function tryMatchCastingSkill(damagePacket, lastAttackTime)
    local damageTarget = damagePacket.Target
    local castingSkills = getCastingSkills()
    
    Log('DEBUG', string.format('[Matching] Searching for casting skill candidates with UsedBy: %s, Target: %s', 
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
        if not skill.TargetHistory then skill.TargetHistory = {} end
        if not tableContains(skill.TargetHistory, damageTarget) then
            table.insert(skill.TargetHistory, damageTarget)
        end
        
        -- 캐스팅 스킬 테이블 업데이트
        getCastingSkills()[key] = skill
        
        Log('INFO', string.format('[Matching] Casting skill changed to Channeling: %s UsedBy: %s Target: %s → %s NextTarget: %s TimeDiff: %.0fms',
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
        
        getCastingSkills()[key] = skill
        
        Log('INFO', string.format('[Matching] Instant casting skill matched: %s UsedBy: %s Target: %s NextTarget: %s State: %s TimeDiff: %.0fms',
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
        
        getCastingSkills()[key] = skill
        
        Log('INFO', string.format('[Matching] TargetCasting skill matched: %s UsedBy: %s Target: %s State: %s TargetingCount: %d',
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
                getCastingSkills()[key] = skill
                
                Log('INFO', string.format('[Matching] Lazy Casting skill matched: %s UsedBy: %s Target: %s NextTarget: %s TimeDiff: %.0fms',
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
                getCastingSkills()[key] = skill
                
                Log('INFO', string.format('[Matching] Area Casting skill matched: %s UsedBy: %s AreaTarget: %s ActualTarget: %s TimeDiff: %.0fms',
                    skill.SkillName, skill.UsedBy, skill.CurrentTarget, damageTarget, timeDiff))
                
                return {Key = key, Skill = skill}
            end
        end
        
        -- C. 매칭되지 않거나 시간 초과된 스킬들 정리 (시간 범위 확장)
        if timeDiff > 15000 then  -- 15초 후 정리
            getCastingSkills()[key] = nil
            Log('INFO', string.format('[Matching] Cleaned up expired lazy casting skill: %s (TimeDiff: %.0fms)', 
                skill.SkillName, timeDiff))
        elseif timeDiff > 3000 then  -- 3초 후부터 대기 상태 로깅
            Log('DEBUG', string.format('[Matching] Lazy casting skill waiting for damage: %s (TimeDiff: %.0fms)', 
                skill.SkillName, timeDiff))
        end
    end
    
    return nil
end

-- 2. 채널링 스킬 매칭 시도
local function tryMatchChannelingSkill(damagePacket, lastAttackTime)
    local bestMatch = nil
    local bestDiff = math.huge
    local castingSkills = getCastingSkills()
    
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
            getCastingSkills()[key] = skill
        elseif skill.State == SkillState.Idle then
            -- 채널링 스킬이 Idle 상태인 경우, 채널링이 끝난 것으로 간주하고 제거
            getCastingSkills()[key] = nil
        end
        
        return {Key = key, Skill = skill}
    end
    
    return nil
end

-- 3. 설치물 매칭 시도
local function tryMatchInstallationByData(damagePacket, lastAttackTime)
    local matchedInstallation = nil
    local installationsByOwner = getInstallationsByOwner()
    local installationsByTarget = getInstallationsByTarget()
    
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
        Log('INFO', string.format('[Matching] Installation matched by data: %s InstallationId: %s Owner: %s Target: %s TimeDiff: %.0fms',
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

-- 4. 즉시 스킬 매칭 시도
local function tryMatchInstantSkillSequential(damagePacket, lastAttackTime)
    Log('DEBUG', string.format('[Matching] Sequential matching for user %s', damagePacket.UsedBy))
    
    local MAX_INSTANT_SKILL_TIME_DIFF_MS = 2000
    local activeSkills = getActiveSkills()
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

-- =============================================================================
-- 메인 매칭 함수
-- =============================================================================

function MatchDamageToSkill(damagePacket, lastAttackTime)
    -- 참고: CleanupOldSkills는 프레임워크에서 자동으로 호출됩니다.
    local damageTarget = damagePacket.Target
    Log('INFO', string.format('[Matching] Matching damage packet: from %s to %s', damagePacket.UsedBy, damageTarget))
    
    -- 1. 캐스팅 스킬 매칭 시도 (최우선)
    local castingMatch = tryMatchCastingSkill(damagePacket, lastAttackTime)
    if castingMatch then
        Log('INFO', string.format('[Matching] Casting skill matched: [%s] %s for damage %d', 
            castingMatch.Key, castingMatch.Skill.SkillName, damagePacket.Damage))
        damagePacket.SkillName = castingMatch.Skill.SkillName
        return damagePacket
    end
    
    -- 2. 채널링 스킬 매칭 시도
    local channelingMatch = tryMatchChannelingSkill(damagePacket, lastAttackTime)
    if channelingMatch then
        Log('INFO', string.format('[Matching] Channeling skill matched: [%s] %s for damage %d', 
            channelingMatch.Key, channelingMatch.Skill.SkillName, damagePacket.Damage))
        damagePacket.SkillName = channelingMatch.Skill.SkillName
        return damagePacket
    end
    
    -- 3. 설치물 스킬 매칭 시도
    local installationMatch = tryMatchInstallationByData(damagePacket, lastAttackTime)
    if installationMatch then
        Log('INFO', string.format('[Matching] Installation skill matched: [%s] %s for damage %d', 
            installationMatch.Key, installationMatch.Skill.SkillName, damagePacket.Damage))
        damagePacket.SkillName = installationMatch.Skill.SkillName
        return damagePacket
    end
    
    -- 4. 활성 스킬 매칭 시도 (기존 활성 스킬에서)
    local instantMatch = tryMatchInstantSkillSequential(damagePacket, lastAttackTime)
    if instantMatch then
        Log('INFO', string.format('[Matching] Instant skill matched: [%s] %s for damage %d', 
            instantMatch.Key, instantMatch.Skill.SkillName, damagePacket.Damage))
        damagePacket.SkillName = instantMatch.Skill.SkillName
        return damagePacket
    end
    
    Log('INFO', string.format('[Matching] No skill match found for damage From %s To %s Damage %d', 
        damagePacket.UsedBy, damagePacket.Target, damagePacket.Damage))
    return damagePacket
end

print("[INFO] SkillMatcher with full matching algorithms loaded successfully")

_G.MatchDamageToSkill = MatchDamageToSkill
end

