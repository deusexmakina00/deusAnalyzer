-- SkillMatcher Framework - 공통 기능 및 유틸리티
-- 사용자는 이 파일을 수정하지 않고, skill_matcher.lua에서 매칭 로직만 구현하면 됩니다.

Log('INFO', '=== SkillMatcher Framework Loading ===')

-- =============================================================================
-- 상수 및 열거형 정의
-- =============================================================================

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

local HP_CHANGE_TYPE = {
    Damage = "Damage",
    Heal = "Heal",
    Unknown = "Unknown"
}

-- 설정 상수
local SKILL_TIMEOUT_SECONDS = 10.0
local INSTALLATION_TIMEOUT_SECONDS = 30.0
local combinedPacketTimeout = 5.0

-- =============================================================================
-- 전역 데이터 저장소
-- =============================================================================

local castingSkills = {}              -- string -> ActiveSkillInfo
local activeSkills = {}               -- string -> table[datetime -> ActiveSkillInfo]
local installationsByOwner = {}       -- string -> InstallationInfo[]
local installationsByTarget = {}      -- string -> InstallationInfo[]
local pendingDamages = {}             -- queue<SkillDamagePacket>

-- 패킷 조합을 위한 임시 저장소
local pendingSkillActions = {}        -- UsedBy -> SkillActionPacket[]
local pendingSkillStates = {}         -- UsedBy -> SkillStatePacket[]
local pendingChangeHp = {}            -- Target -> ChangeHpPacket[]

-- =============================================================================
-- 유틸리티 함수들 (공통)
-- =============================================================================

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

local function isTargetMatchSimple(skillTarget, damageTarget)
    return isTargetMatch(skillTarget, "00000000", damageTarget)
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

-- =============================================================================
-- 객체 생성 함수들
-- =============================================================================

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

local function createInstallationInfo(installationId, owner, target, skillName, registeredAt)
    return {
        InstallationId = installationId,
        Owner = owner,
        Target = target,
        SkillName = skillName,
        RegisteredAt = registeredAt
    }
end

-- =============================================================================
-- 스킬명 파싱 및 키 관리
-- =============================================================================

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

local function getSkillKey(usedBy, startTime)
    return usedBy .. "_" .. tostring(startTime)
end

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

-- =============================================================================
-- 스킬 상태 관리
-- =============================================================================

local function updateSkillState(skillKey, packet, newState, receivedAt)
    local existingSkill = castingSkills[skillKey]
    if not existingSkill then
        Log('WARN', string.format('[Framework] Skill not found for update: %s', skillKey))
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
    
    Log('DEBUG', string.format('[Framework] Updated skill state: %s → %s (Key: %s)', 
        existingSkill.SkillName, newState, skillKey))
end

-- =============================================================================
-- 설치물 관리
-- =============================================================================

local function isInstallationSkill(skillInfo)
    -- 1. 기본 조건 체크
    if skillInfo.Owner == "00000000" or not skillInfo.Target or string.len(skillInfo.UsedBy) ~= 8 then
        return false
    end
    
    -- 2. 자기 대상 버프 제외: 자기 자신 대상 (마나회복 등)
    if skillInfo.UsedBy == skillInfo.Owner and skillInfo.Target == skillInfo.Owner then
        Log('DEBUG', string.format('[Framework] Skipped self-buff skill: %s Self-target: %s', 
            skillInfo.SkillName, skillInfo.Owner))
        return false
    end
    
    -- 3. Hit 패턴 특별 처리 (중요!)
    local baseName, state, skillType = parseSkillName(skillInfo.SkillName)
    if state == SkillState.Hit then
        -- Hit 상태의 설치물은 항상 허용 (채널링 관련 체크 건너뛰기)
        local isInstallationHit = skillInfo.UsedBy ~= skillInfo.Owner and skillInfo.Target ~= skillInfo.Owner
        
        if isInstallationHit then
            Log('DEBUG', string.format('[Framework] Detected installation Hit: %s InstallationId: %s Owner: %s Target: %s', 
                skillInfo.SkillName, skillInfo.UsedBy, skillInfo.Owner, skillInfo.Target))
            return true -- Hit 설치물은 무조건 등록
        end
        
        -- Hit_Lightning 등 자기 준비 신호는 설치물이 아님
        if skillInfo.UsedBy == skillInfo.Owner then
            Log('DEBUG', string.format('[Framework] Skipped self Hit signal: %s UsedBy: %s Owner: %s', 
                skillInfo.SkillName, skillInfo.UsedBy, skillInfo.Owner))
            return false
        end
    end
    
    -- 4. 일반 설치물 패턴
    local isRealInstallation = skillInfo.UsedBy ~= skillInfo.Owner and skillInfo.Target ~= skillInfo.Owner
    
    if isRealInstallation then
        Log('DEBUG', string.format('[Framework] Detected real installation: %s InstallationId: %s Owner: %s Target: %s', 
            skillInfo.SkillName, skillInfo.UsedBy, skillInfo.Owner, skillInfo.Target))
    end
    
    return isRealInstallation
end

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
    
    Log('INFO', string.format('[Framework] Registered installation: %s InstallationId: %s Owner: %s Target: %s', 
        skillInfo.SkillName, skillInfo.UsedBy, skillInfo.Owner, skillInfo.Target))
end

-- =============================================================================
-- 정리 함수
-- =============================================================================

-- 오래된 스킬 정리 (프레임워크 내부 함수)
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
            Log('DEBUG', string.format('[Framework] Removed expired active skill: %s', tostring(key)))
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
        Log('INFO', string.format('[Framework] Removed expired casting skill: %s UsedBy: %s Target: %s', 
            skill.SkillName, skill.UsedBy, skill.CurrentTarget))
    end
    
    -- 패킷 조합용 임시 저장소 정리
    for usedBy, packets in pairs(pendingSkillActions) do
        local validPackets = {}
        for _, p in pairs(packets) do
            if (lastAt - p.time) <= combinedPacketTimeout then
                table.insert(validPackets, p)
            end
        end
        pendingSkillActions[usedBy] = validPackets
    end
    
    for usedBy, packets in pairs(pendingSkillStates) do
        local validPackets = {}
        for _, p in pairs(packets) do
            if (lastAt - p.time) <= combinedPacketTimeout then
                table.insert(validPackets, p)
            end
        end
        pendingSkillStates[usedBy] = validPackets
    end
    
    for target, packets in pairs(pendingChangeHp) do
        local validPackets = {}
        for _, p in pairs(packets) do
            if (lastAt - p.time) <= combinedPacketTimeout then
                table.insert(validPackets, p)
            end
        end
        pendingChangeHp[target] = validPackets
    end
    
    -- 설치물 정리 (30초 이상 된 것들)
    for owner, installations in pairs(installationsByOwner) do
        local validInstallations = {}
        for _, inst in pairs(installations) do
            if (lastAt - inst.RegisteredAt) <= INSTALLATION_TIMEOUT_SECONDS then
                table.insert(validInstallations, inst)
            else
                Log('DEBUG', string.format('[Framework] Removed expired installation: %s', inst.SkillName))
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

-- =============================================================================
-- DamageModel 생성 및 콜백 헬퍼 함수들
-- =============================================================================
function NotifyDamageMatched(usedBy, target, damage, skillName, selfTarget, flagBytes)
    Log('INFO', string.format('[DamageMatched] %s -> %s | %s | Damage: %d', 
        usedBy or "Unknown", 
        target or "Unknown", 
        skillName or "Unknown Skill", 
        damage or 0))
    local damageModel
    
    damageModel = CreateDamageModel(usedBy, target, damage, skillName, flagBytes, selfTarget)
    -- 콜백 호출
    OnDamageMatchedCallback(damageModel)
end
-- =============================================================================
-- C#에서 호출되는 필수 Entrypoint 함수들
-- =============================================================================

-- SkillActionPacket 처리
function EnqueueSkillAction(skillAction, lastAt)
    Log('INFO', string.format('[Framework] EnqueueSkillAction: %s', 
        skillAction.ToString()))
end

-- SkillStatePacket 처리
function EnqueueSkillState(skillStatePacket, lastAt)
    Log('DEBUG', string.format('[Framework] EnqueueSkillState: %s', skillStatePacket.ToString()))
end

-- ChangeHpPacket 처리 (EnqueueHpChange와 EnqueueChangeHp 통합)
function EnqueueHpChange(changeHpPacket, lastAt)
    Log('DEBUG', string.format('[Framework] EnqueueHpChange: %s', 
        changeHpPacket.ToString()))
end

-- SkillInfoPacket 처리
function EnqueueSkillInfo(skillInfo, lastAt)
    Log('DEBUG', string.format('[Framework] EnqueueSkillInfo: %s', 
        skillInfo.ToString()))
end

-- SkillDamagePacket 처리
function EnqueueDamage(damage, lastAttackTime)
    Log('DEBUG', string.format('[Framework] EnqueueDamage: %s', damage.ToString()))
    
    -- CleanupOldSkills 호출 (매칭 시작 전)
    CleanupOldSkills(lastAttackTime)
    
    -- MatchDamageToSkill 호출하여 매칭 시도
    local matchedDamage = nil
    if _G.MatchDamageToSkill then
        matchedDamage = _G.MatchDamageToSkill(damage, lastAttackTime)
    else
        Log('WARN', '[Framework] MatchDamageToSkill function not found - using original damage')
        matchedDamage = damage
    end
    
    -- 매칭 결과가 있으면 DamageModel 생성하여 전송
    if matchedDamage then
        NotifyDamageMatched(
            matchedDamage.UsedBy,
            matchedDamage.Target,
            matchedDamage.Damage,
            matchedDamage.SkillName,
            1,
            matchedDamage.FlagBytes
        )
    else
        Log('DEBUG', '[Framework] No damage match result - skipping DamageModel creation')
    end
end

-- =============================================================================
-- 공개 API - 사용자 정의 매칭 로직에서 사용 가능
-- =============================================================================

-- 상수 노출
_G.SkillState = SkillState
_G.SkillType = SkillType
_G.HP_CHANGE_TYPE = HP_CHANGE_TYPE

-- 유틸리티 함수 노출
_G.getTimeDiffMs = getTimeDiffMs
_G.isTargetMatch = isTargetMatch
_G.isTargetMatchSimple = isTargetMatchSimple
_G.determineHpChangeType = determineHpChangeType
_G.isHealingSkill = isHealingSkill

-- 데이터 접근 함수
_G.getCastingSkills = function() return castingSkills end
_G.getActiveSkills = function() return activeSkills end
_G.getInstallationsByOwner = function() return installationsByOwner end
_G.getInstallationsByTarget = function() return installationsByTarget end
_G.getPendingDamages = function() return pendingDamages end

-- 객체 생성 함수 노출
_G.createActiveSkillInfo = createActiveSkillInfo
_G.createInstallationInfo = createInstallationInfo

-- 스킬 관리 함수 노출
_G.parseSkillName = parseSkillName
_G.getSkillKey = getSkillKey
_G.findActiveSkillKey = findActiveSkillKey
_G.updateSkillState = updateSkillState

-- 설치물 관리 함수 노출
_G.isInstallationSkill = isInstallationSkill
_G.registerInstallation = registerInstallation

-- 패킷 조합 함수 노출
_G.tryCreateDamageModelFromCombinedPackets = tryCreateDamageModelFromCombinedPackets

-- 오래된 스킬 정리 함수 노출
_G.CleanupOldSkills = CleanupOldSkills

-- 패킷 저장소 접근
_G.getPendingSkillActions = function() return pendingSkillActions end
_G.getPendingSkillStates = function() return pendingSkillStates end
_G.getPendingChangeHp = function() return pendingChangeHp end

-- 상수 노출
_G.SKILL_TIMEOUT_SECONDS = SKILL_TIMEOUT_SECONDS
_G.INSTALLATION_TIMEOUT_SECONDS = INSTALLATION_TIMEOUT_SECONDS
_G.combinedPacketTimeout = combinedPacketTimeout

-- 필수 C# Entrypoint 함수들 노출
_G.EnqueueSkillAction = EnqueueSkillAction
_G.EnqueueSkillState = EnqueueSkillState
_G.EnqueueHpChange = EnqueueHpChange
_G.EnqueueSkillInfo = EnqueueSkillInfo
_G.EnqueueDamage = EnqueueDamage

_G.MatchDamageToSkill = MatchDamageToSkill
Log('INFO', '=== SkillMatcher Framework Loaded ===')
Log('INFO', 'All essential C# entrypoints available')
Log('INFO', 'Ready for user-defined matching algorithms')
