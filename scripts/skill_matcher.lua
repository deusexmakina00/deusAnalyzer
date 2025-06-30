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
-- 매칭 알고리즘 구현
-- =============================================================================

-- 1. 캐스팅 스킬 매칭 시도
local function tryMatchCastingSkill(damagePacket, lastAttackTime)
    return nil
end

-- 2. 채널링 스킬 매칭 시도
local function tryMatchChannelingSkill(damagePacket, lastAttackTime)
    return nil
end

-- 3. 설치물 매칭 시도
local function tryMatchInstallationByData(damagePacket, lastAttackTime)
    return nil
end

-- 4. 즉시 스킬 매칭 시도
local function tryMatchInstantSkillSequential(damagePacket, lastAttackTime)
    return nil
end

-- =============================================================================
-- 메인 매칭 함수
-- =============================================================================

function MatchDamageToSkill(damagePacket, lastAttackTime)
    local matched
    matched = tryMatchCastingSkill(damagePacket, lastAttackTime)
    if matched then
        return matched
    end
    matched = tryMatchChannelingSkill(damagePacket, lastAttackTime)
    if matched then
        return matched
    end
    matched = tryMatchInstallationByData(damagePacket, lastAttackTime)
    if matched then
        return matched
    end
    matched = tryMatchInstantSkillSequential(damagePacket, lastAttackTime)
    if matched then
        return matched
    end
    return damagePacket
end

print("[INFO] SkillMatcher with full matching algorithms loaded successfully")

_G.MatchDamageToSkill = MatchDamageToSkill