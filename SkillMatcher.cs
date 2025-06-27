using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NLog;
using PacketDotNet.Tcp;

namespace PacketCapture;

/// <summary>
/// 스킬 상태를 나타내는 열거형
/// </summary>
public enum SkillState
{
    /// <summary>스킬이 시전 중 (Casting)</summary>
    Casting,

    /// <summary>스킬이 타겟팅 중 (Targeting)</summary>
    Targeting,

    /// <summary>스킬이 종료 중 (End)</summary>
    Ending,

    /// <summary>스킬이 활성화됨 (Hit)</summary>
    Hit,

    /// <summary>스킬이 마무리 됨. (Idle)</summary>
    Idle,

    /// <summary>즉시 스킬 (일반 스킬, 바로 사용되고 제거됨)</summary>
    Instant,
}

/// <summary>
/// 스킬 타입을 나타내는 열거형
/// </summary>
public enum SkillType
{
    /// <summary> 캐스팅 스킬 (_Casting -> _End -> Instance Hit) </summary>
    Casting,

    /// <summary>
    /// 캐스팅이 끝난 후 지연된 데미지가 발생
    /// </summary>
    LazyCasting,

    /// <summary>캐스팅 스킬 (_Casting -> _Targeting -> _End -> _Hit -> 완료)</summary>
    TargetCasting,

    /// <summary>채널링 스킬 (_Casting -> _End -> Idle)</summary>
    Channeling,

    /// <summary>즉시 스킬 (일반 스킬)</summary>
    Instant,

    /// <summary>도트 스킬 (지속 피해)</summary>
    Dot,
}

/// <summary>
/// 활성 스킬 정보를 저장하는 레코드
/// </summary>
/// <param name="UsedBy">사용자 ID</param>
/// <param name="Target">대상 ID</param>
/// <param name="SkillName">스킬 이름 (접두사 제거)</param>
/// <param name="State">스킬 상태</param>
/// <param name="Type">스킬 타입</param>
/// <param name="LastStateTime">마지막 상태 변경 시간</param>
/// <param name="IsUsing">스킬이 사용 됬는지 여부</param>
/// <param name="TargetingCount">타겟팅 횟수 (캐스팅 스킬용)</param>
public record ActiveSkillInfo(
    string UsedBy,
    string Target,
    string SkillName,
    SkillState State,
    SkillType Type,
    DateTime LastStateTime,
    bool IsUsing = false,
    int NoDamageCount = 0,
    int TargetingCount = 0
);

/// <summary>
/// 스킬 매칭 결과를 나타내는 레코드
/// </summary>
/// <param name="Key">스킬 키 (skill_name + used_by + target)</param>
/// <param name="Skill">매칭된 스킬 정보</param>
public readonly record struct SkillMatchResult(string Key, ActiveSkillInfo? Skill);

/// <summary>
/// 고급 스킬 매처 클래스
/// 채널링, 캐스팅, 즉시 스킬을 모두 처리하는 통합 시스템
/// </summary>
public sealed class SkillMatcher
{
    private static readonly Logger logger = LogManager.GetCurrentClassLogger();

    // 스킬 상태 관리
    private readonly ConcurrentDictionary<string, ActiveSkillInfo> _castingSkills = new();
    private readonly ConcurrentDictionary<
        string,
        ConcurrentDictionary<DateTime, ActiveSkillInfo>
    > _activeSkills = new();

    private readonly ConcurrentQueue<(SkillDamagePacket, DateTime)> _pendingDamages = new();

    public event Action<SkillDamagePacket>? OnDamageMatched;

    // 설정 상수
    private const double SKILL_TIMEOUT_SECONDS = 10.0;

    /// <summary>
    /// Target 값을 정규화합니다 (마지막 바이트를 00으로 변경)
    /// </summary>
    private string NormalizeTarget(string target)
    {
        if (string.IsNullOrEmpty(target) || target.Length < 8 || target == "ffffffff")
            return target;

        // 마지막 바이트를 00으로 변경하여 정규화
        return target.Substring(0, 6) + "00";
    }

    /// <summary>
    /// 두 Target 값이 매칭되는지 확인합니다 (유연한 매칭)
    /// </summary>
    private bool IsTargetMatch(string skillTarget, string damageTarget)
    {
        // 둘 중 하나가 00000000이면 매칭
        if (
            skillTarget == "00000000"
            || damageTarget == "00000000"
            || damageTarget == "ffffffff"
            || skillTarget == "ffffffff"
        )
            return true;

        // 정확히 일치하면 매칭
        return skillTarget == damageTarget;
    }

    /// <summary>
    /// 스킬 키를 생성합니다 (skill_name + used_by + target + sequence)
    /// </summary>
    private static string GetSkillKey(string skillName, string usedBy, string target)
    {
        return $"{skillName}_{usedBy}_{target}";
    }

    public void EnqueueDamage(SkillDamagePacket damage, DateTime lastAttackTime)
    {
        _pendingDamages.Enqueue((damage, lastAttackTime));
        ProcessPendingDamages();
    }

    private void ProcessPendingDamages()
    {
        while (_pendingDamages.TryDequeue(out var item))
        {
            var (damage, lastAttackTime) = item;
            var matched = MatchDamageToSkill(damage, lastAttackTime);
            if (matched != null)
            {
                // 매칭된 데미지를 콜백으로 전달.
                OnDamageMatched?.Invoke(damage);
            }
        }
    }

    private (string baseSkillName, SkillState state, SkillType type) ParseSkillName(
        string skillName
    )
    {
        if (skillName.EndsWith("_Casting", StringComparison.OrdinalIgnoreCase))
            return (skillName[..^8], SkillState.Casting, SkillType.Casting);
        if (skillName.EndsWith("_Targeting", StringComparison.OrdinalIgnoreCase))
            return (skillName[..^10], SkillState.Targeting, SkillType.TargetCasting);
        if (skillName.EndsWith("_End", StringComparison.OrdinalIgnoreCase))
            return (skillName[..^4], SkillState.Ending, SkillType.Casting);
        if (skillName.EndsWith("_Hit", StringComparison.OrdinalIgnoreCase))
            return (skillName[..^4], SkillState.Hit, SkillType.Casting);
        if (skillName.Equals("Idle", StringComparison.OrdinalIgnoreCase))
            return (skillName, SkillState.Idle, SkillType.Instant);

        // 기본값: 즉시 스킬
        return (skillName, SkillState.Instant, SkillType.Instant);
    }

    public void EnqueueSkill(SkillInfoPacket skill, DateTime lastAt)
    {
        if (string.IsNullOrEmpty(skill.SkillName))
        {
            return;
        }

        var (baseSkillName, state, type) = ParseSkillName(skill.SkillName);
        var usedBy = skill.UsedBy;
        var target = NormalizeTarget(skill.Target);
        var skillKey = GetSkillKey(baseSkillName, usedBy, target);
        if (type != SkillType.Instant)
        {
            // 캐스팅/채널링/타게팅 스킬 처리
            if (_castingSkills.TryGetValue(skillKey, out var existingSkill))
            {
                var targetingCount = existingSkill.TargetingCount;

                if (state == SkillState.Targeting)
                {
                    // 타겟팅 상태인 경우 타겟팅 횟수 증가
                    targetingCount++;
                }
                if (state == SkillState.Hit)
                {
                    targetingCount--;
                    if (targetingCount == 0)
                    {
                        _castingSkills.TryRemove(skillKey, out _);
                        logger.Info(
                            $"[SkillMatcher] Removing Casting Skill: {existingSkill.SkillName} UsedBy: {existingSkill.UsedBy} Target: {existingSkill.Target}"
                        );
                        return;
                    }
                }
                _castingSkills[skillKey] = existingSkill with
                {
                    State = state,
                    Type = type,
                    LastStateTime = lastAt,
                    TargetingCount = targetingCount,
                };
                logger.Info(
                    $"[SkillMatcher] Updating Casting[{type}] Skill: {existingSkill.SkillName} UsedBy: {existingSkill.UsedBy} Target: {existingSkill.Target} (State: {state})"
                );
            }
            else
            {
                // 새로운 스킬 등록
                _castingSkills.TryAdd(
                    skillKey,
                    new ActiveSkillInfo(usedBy, target, baseSkillName, state, type, lastAt)
                );

                logger.Info(
                    $"[SkillMatcher] Registering Casting[{type}] Skill: {baseSkillName} UsedBy: {usedBy} Target: {target} (State: {state})"
                );
            }
        }
        else if (baseSkillName.Equals("Idle", StringComparison.OrdinalIgnoreCase))
        {
            // Idle 스킬 처리
            if (_castingSkills.Count != 0)
            {
                var lastChanneling = _castingSkills.LastOrDefault(v =>
                    usedBy == v.Value.UsedBy
                    && target == v.Value.Target
                    && v.Value is { Type: SkillType.Channeling, State: SkillState.Ending }
                );

                if (lastChanneling.Key != null)
                {
                    _castingSkills[lastChanneling.Key] = lastChanneling.Value with
                    {
                        LastStateTime = lastAt,
                        State = SkillState.Idle,
                    };

                    logger.Info(
                        $"[SkillMatcher] Updating Casting[{lastChanneling.Value.Type}] Skill: {lastChanneling.Value.SkillName} (State: {SkillState.Idle})"
                    );
                }
            }
        }
        else
        {
            // 즉시 스킬 처리
            var userSkill = _activeSkills.GetOrAdd(
                usedBy,
                _ => new ConcurrentDictionary<DateTime, ActiveSkillInfo>()
            );
            userSkill.TryAdd(
                lastAt,
                new ActiveSkillInfo(
                    usedBy,
                    target,
                    skill.SkillName,
                    SkillState.Instant,
                    SkillType.Instant,
                    lastAt
                )
            );
            logger.Info(
                $"[SkillMatcher] Registering Instant Skill: {skill.SkillName} (UsedBy: {usedBy}, Target: {target})"
            );
        }
    }

    /// <summary>
    /// 데미지 패킷과 스킬을 매칭합니다
    /// </summary>
    /// <param name="damagePacket">데미지 패킷</param>
    /// <param name="lastAttackTime">마지막 공격 시각</param>
    /// <returns>스킬 이름이 설정된 데미지 패킷</returns>
    public SkillDamagePacket MatchDamageToSkill(
        SkillDamagePacket damagePacket,
        DateTime lastAttackTime
    )
    {
        // 0. 도트 스킬은 별도로 처리
        if (damagePacket.IsDot())
            return damagePacket;

        var damageTarget = NormalizeTarget(damagePacket.Target);
        logger.Info(
            $"[SkillMatcher] Matching damage packet: from {damagePacket.UsedBy} to {damageTarget}"
        );

        // 1. 캐스팅 스킬 매칭 시도 (최우선)
        var castingMatch = TryMatchCastingSkill(damagePacket, lastAttackTime);
        if (castingMatch.HasValue)
        {
            logger.Info(
                $"[SkillMatcher] Casting skill matched: [{castingMatch.Value.Key}] {castingMatch.Value.Skill?.SkillName} for damage {damagePacket.Damage}"
            );
            damagePacket.SkillName = castingMatch.Value.Skill?.SkillName ?? "";
            return damagePacket;
        }

        // 2. 채널링 스킬 매칭 시도
        var channelingMatch = TryMatchChannelingSkill(damagePacket, lastAttackTime);
        if (channelingMatch.HasValue)
        {
            logger.Info(
                $"[SkillMatcher] Channeling skill matched: [{channelingMatch.Value.Key}] {channelingMatch.Value.Skill?.SkillName} for damage {damagePacket.Damage}"
            );
            damagePacket.SkillName = channelingMatch.Value.Skill?.SkillName ?? "";
            return damagePacket;
        }
        // 3. 즉시 스킬 매칭 시도 (순차적 매칭)
        var instantMatch = TryMatchInstantSkillSequential(damagePacket, lastAttackTime);
        if (instantMatch.HasValue)
        {
            logger.Info(
                $"[SkillMatcher] Instant skill matched: [{instantMatch.Value.Key}] {(
                    instantMatch.Value.Skill?.Type == SkillType.Dot ? "Dot"
                    : instantMatch.Value.Skill?.SkillName
                    )} for damage {damagePacket.Damage}"
            );
            damagePacket.SkillName = instantMatch.Value.Skill?.SkillName ?? "";

            return damagePacket;
        }
        var lazyCastingMatch = TryMatchLazyCastingSkill(damagePacket, lastAttackTime);
        if (lazyCastingMatch.HasValue)
        {
            logger.Info(
                $"[SkillMatcher] Lazy Casting skill matched: [{lazyCastingMatch.Value.Key}] {lazyCastingMatch.Value.Skill?.SkillName} for damage {damagePacket.Damage}"
            );

            damagePacket.SkillName = lazyCastingMatch.Value.Skill?.SkillName ?? "";
            return damagePacket;
        }
        logger.Info(
            $"[SkillMatcher] No skill match found for damage From {damagePacket.UsedBy} To {damagePacket.Target} Damage {damagePacket.Damage}"
        );
        return damagePacket;
    }

    /// <summary>
    /// 스킬 매칭을 수행합니다.
    /// </summary>
    /// <typeparam name="TKey"></typeparam>
    /// <param name="source">스킬 정보 소스</param>
    /// <param name="damagePacket">데미지 패킷</param>
    /// <param name="lastAttackTime">마지막 공격 시각</param>
    /// <param name="filter">스킬 필터</param>
    /// <param name="onMatched">스킬 매칭 콜백</param>
    /// <param name="maxTimeDiffMs">최대 시간 차 (밀리초)</param>
    /// <returns></returns>
    private SkillMatchResult? MatchSkillCore<TKey>(
        IEnumerable<KeyValuePair<TKey, ActiveSkillInfo>> source,
        SkillDamagePacket damagePacket,
        DateTime lastAttackTime,
        Func<ActiveSkillInfo, bool> filter,
        Action<TKey, ActiveSkillInfo>? onMatched = null,
        double? maxTimeDiffMs = null
    )
    {
        var damageTarget = NormalizeTarget(damagePacket.Target);

        var candidates = source
            .Where(kvp =>
                IsTargetMatch(kvp.Value.Target, damageTarget)
                && kvp.Value.UsedBy == damagePacket.UsedBy
                && filter(kvp.Value)
            )
            .Select(kvp => new
            {
                kvp.Key,
                kvp.Value,
                Diff = Math.Abs((lastAttackTime - kvp.Value.LastStateTime).TotalMilliseconds),
            });

        if (maxTimeDiffMs.HasValue)
        {
            candidates = candidates.Where(x => x.Diff < maxTimeDiffMs.Value);
        }

        var match = candidates.OrderBy(x => x.Diff).FirstOrDefault();

        if (match == null)
            return null;

        onMatched?.Invoke(match.Key, match.Value);

        return new SkillMatchResult(match.Key?.ToString() ?? "", match.Value);
    }

    /// <summary>
    /// 캐스팅 스킬 매칭을 시도합니다
    /// </summary>
    private SkillMatchResult? TryMatchCastingSkill(
        SkillDamagePacket damagePacket,
        DateTime lastAttackTime
    )
    {
        if (_castingSkills.IsEmpty)
        {
            return null;
        }
        var damageTarget = NormalizeTarget(damagePacket.Target);

        logger.Debug(
            $"[SkillMatcher] Searching for casting skill candidates with UsedBy: {damagePacket.UsedBy}, Target: {damageTarget}"
        );
        // 1. 채널링 패턴: Casting 상태에서 Damage가 들어오면 Channeling으로 전이
        var channelingCandidate = _castingSkills
            .Where(kvp =>
                IsTargetMatch(kvp.Value.Target, damageTarget)
                && kvp.Value.UsedBy == damagePacket.UsedBy
                && kvp.Value.Type == SkillType.Casting
                && kvp.Value.State == SkillState.Casting
            )
            .OrderBy(kvp => Math.Abs((lastAttackTime - kvp.Value.LastStateTime).TotalMilliseconds))
            .FirstOrDefault();

        if (channelingCandidate.Value != null)
        {
            _castingSkills[channelingCandidate.Key] = channelingCandidate.Value with
            {
                Type = SkillType.Channeling,
            };
            logger.Info(
                $"[SkillMatcher] Casting skill changed to Channeling: {channelingCandidate.Value.SkillName} UsedBy: {channelingCandidate.Value.UsedBy} Target: {channelingCandidate.Value.Target}"
            );
            return null; // 채널링으로 전이되었으므로 null 반환
        }
        // 2. 타게팅 캐스팅 패턴: TargetCasting + Ending 상태에서만 매칭
        var targetingCandidate = _castingSkills
            .Where(kvp =>
                IsTargetMatch(kvp.Value.Target, damageTarget)
                && kvp.Value.UsedBy == damagePacket.UsedBy
                && !kvp.Value.IsUsing
                && kvp.Value.Type == SkillType.Casting
                && kvp.Value.State == SkillState.Ending
            )
            .OrderBy(kvp => Math.Abs((lastAttackTime - kvp.Value.LastStateTime).TotalMilliseconds))
            .FirstOrDefault();

        if (targetingCandidate.Value != null)
        {
            _castingSkills[targetingCandidate.Key] = targetingCandidate.Value with
            {
                IsUsing = true,
                LastStateTime = lastAttackTime,
            };
            return new SkillMatchResult(targetingCandidate.Key, targetingCandidate.Value);
        }
        // 3. Lazy 캐스팅 연타 패턴: Casting + Ending 상태에서 매칭
        var lazyCandidate = _castingSkills
            .Where(kvp =>
                IsTargetMatch(kvp.Value.Target, damageTarget)
                && kvp.Value.UsedBy == damagePacket.UsedBy
                && kvp.Value.Type == SkillType.Casting
                && kvp.Value.State == SkillState.Ending
            )
            .OrderBy(kvp => Math.Abs((lastAttackTime - kvp.Value.LastStateTime).TotalMilliseconds))
            .FirstOrDefault();

        if (lazyCandidate.Value != null)
        {
            if (lazyCandidate.Value.IsUsing == false)
            {
                _castingSkills.TryRemove(lazyCandidate.Key, out _);
                logger.Info(
                    $"[SkillMatcher] Removing Lazy Casting Skill: {lazyCandidate.Value.SkillName} UsedBy: {lazyCandidate.Value.UsedBy} Target: {lazyCandidate.Value.Target}"
                );

                return null;
            }
            _castingSkills[lazyCandidate.Key] = lazyCandidate.Value with
            {
                IsUsing = true,
                LastStateTime = lastAttackTime,
            };
            return new SkillMatchResult(lazyCandidate.Key, lazyCandidate.Value);
        }

        return null;
    }

    /// <summary>
    /// 채널링 스킬 매칭을 시도합니다
    /// </summary>
    private SkillMatchResult? TryMatchChannelingSkill(
        SkillDamagePacket damagePacket,
        DateTime lastAttackTime
    )
    {
        return MatchSkillCore(
            _castingSkills,
            damagePacket,
            lastAttackTime,
            skill => skill.Type == SkillType.Channeling,
            (key, skill) =>
            {
                // 채널링 스킬이 진행 중인 경우 마지막 데미지 시간으로 상태 정보 업데이트
                if (skill.State == SkillState.Casting || skill.State == SkillState.Ending)
                {
                    _castingSkills[key] = skill with { LastStateTime = lastAttackTime };
                }
                if (skill.State == SkillState.Idle)
                {
                    // 채널링 스킬이 Idle 상태인 경우, 채널링이 끝난 것으로 간주하고 제거
                    _castingSkills.TryRemove(key, out _);
                }
            }
        );
    }

    /// <summary>
    /// 지연된 캐스팅 스킬 매칭 - 캐스팅이 완료된 지연된 스킬 매칭
    /// </summary>
    private SkillMatchResult? TryMatchLazyCastingSkill(
        SkillDamagePacket damagePacket,
        DateTime lastAttackTime
    )
    {
        var damageTarget = NormalizeTarget(damagePacket.Target);
        logger.Debug(
            $"[SkillMatcher] Searching for casting skill candidates with UsedBy: {damagePacket.UsedBy}, Target: {damageTarget}"
        );
        return MatchSkillCore(
            _castingSkills,
            damagePacket,
            lastAttackTime,
            skill =>
                !skill.IsUsing
                && skill.Type == SkillType.Casting
                && skill.State == SkillState.Ending,
            (key, skill) =>
            {
                _castingSkills[key] = skill with { LastStateTime = lastAttackTime };
            }
        );
    }

    /// <summary>
    /// 순차적 즉시 스킬 매칭 - (Instant/ Lazy Damage 패턴)
    /// </summary>
    private SkillMatchResult? TryMatchInstantSkillSequential(
        SkillDamagePacket damagePacket,
        DateTime lastAttackTime
    )
    {
        logger.Debug($"[SkillMatcher] Sequential matching for user {damagePacket.UsedBy}");

        const double MAX_INSTANT_SKILL_TIME_DIFF_MS = 2000;
        if (!_activeSkills.TryGetValue(damagePacket.UsedBy, out var userSeqTable))
            return null;

        return MatchSkillCore(
            userSeqTable,
            damagePacket,
            lastAttackTime,
            skill => true,
            (key, skill) =>
            {
                // 즉시 스킬은 매칭 후 제거
                if (skill.Target != "ffffffff")
                {
                    userSeqTable.TryRemove(key, out _);
                }
            },
            MAX_INSTANT_SKILL_TIME_DIFF_MS
        );
    }

    public void CleanupOldSkills(DateTime lastAt)
    {
        foreach (var userSkills in _activeSkills.Values)
        {
            var expired = userSkills
                .Where(skill =>
                    skill.Value.LastStateTime + TimeSpan.FromSeconds(SKILL_TIMEOUT_SECONDS) < lastAt
                )
                .Select(skill => skill.Key)
                .ToList();
            foreach (var lastStateTime in expired)
            {
                userSkills.TryRemove(lastStateTime, out _);
            }
        }
        var expiredCastingKeys = _castingSkills
            .Where(kvp =>
                kvp.Value.LastStateTime + TimeSpan.FromSeconds(SKILL_TIMEOUT_SECONDS) < lastAt
            )
            .Select(kvp => kvp.Key)
            .ToList();
        foreach (var expiredKey in expiredCastingKeys)
        {
            _castingSkills.TryRemove(expiredKey, out var castingSkill);
            logger.Info(
                $"[SkillMatcher] Removed {castingSkill.SkillName} UsagedBy: {castingSkill.UsedBy} Target: {castingSkill.Target}"
            );
        }
    }
}
