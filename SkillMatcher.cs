using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Microsoft.VisualBasic;
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

    /// <summary>캐스팅 스킬 (_Casting -> _Targeting -> _End -> _Hit -> 완료)</summary>
    TargetCasting,

    /// <summary>채널링 스킬 (_Casting -> _End -> Idle)</summary>
    Channeling,

    /// <summary>즉시 스킬 (일반 스킬)</summary>
    Instant,

    /// <summary>도트 스킬 (지속 피해)</summary>
    Dot,

    /// <summary>
    /// 설치형 스킬 (설치 후 일정 시간 동안 유지)
    /// </summary>
    Installation,
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
/// <param name="TargetHistory">타겟 히스토리 (타겟 변경 이력)</param>
/// <param name="StateHistory">상태 히스토리 (상태 변경 이력)</param>
public record ActiveSkillInfo(
    string UsedBy,
    string OriginalTarget,
    string CurrentTarget,
    string NextTarget,
    string SkillName,
    SkillState State,
    SkillType Type,
    DateTime StartTime,
    DateTime LastStateTime,
    bool IsUsing = false,
    int TargetingCount = 0,
    List<string>? TargetHistory = null,
    Dictionary<SkillState, DateTime>? StateHistory = null
)
{
    /// <summary>
    /// 새로운 ActiveSkillInfo 인스턴스를 생성합니다 (기본 컬렉션 포함)
    /// </summary>
    public static ActiveSkillInfo Create(
        string usedBy,
        string originalTarget,
        string currentTarget,
        string nextTarget,
        string skillName,
        SkillState state,
        SkillType type,
        DateTime startTime,
        DateTime lastStateTime,
        bool isUsing = false,
        int targetingCount = 0
    ) =>
        new(
            usedBy,
            originalTarget,
            currentTarget,
            nextTarget,
            skillName,
            state,
            type,
            startTime,
            lastStateTime,
            isUsing,
            targetingCount,
            new List<string> { currentTarget }, // ✅ 현재 타겟으로 초기화
            new Dictionary<SkillState, DateTime> { [state] = startTime } // ✅ 현재 상태로 초기화
        );

    /// <summary>타겟 히스토리 (null 안전 접근)</summary>
    public List<string> GetTargetHistory() => TargetHistory ?? new List<string>();

    /// <summary>상태 히스토리 (null 안전 접근)</summary>
    public Dictionary<SkillState, DateTime> GetStateHistory() =>
        StateHistory ?? new Dictionary<SkillState, DateTime>();

    /// <summary>스킬 상태만 업데이트</summary>
    public ActiveSkillInfo WithState(SkillState newState, DateTime stateTime)
    {
        var stateHistory = GetStateHistory();
        stateHistory[newState] = stateTime;

        return this with
        {
            State = newState,
            LastStateTime = stateTime,
            StateHistory = stateHistory,
        };
    }

    /// <summary>타겟만 업데이트 (히스토리 포함)</summary>
    public ActiveSkillInfo WithTarget(string newTarget, DateTime updateTime)
    {
        var targetHistory = GetTargetHistory();
        if (CurrentTarget != newTarget && !targetHistory.Contains(newTarget))
        {
            targetHistory.Add(newTarget);
        }

        return this with
        {
            CurrentTarget = newTarget,
            LastStateTime = updateTime,
            TargetHistory = targetHistory,
        };
    }

    /// <summary>타겟과 상태를 동시에 업데이트</summary>
    public ActiveSkillInfo WithTargetAndState(
        string newTarget,
        SkillState newState,
        DateTime updateTime
    )
    {
        var targetHistory = GetTargetHistory();
        var stateHistory = GetStateHistory();

        if (CurrentTarget != newTarget && !targetHistory.Contains(newTarget))
        {
            targetHistory.Add(newTarget);
        }

        stateHistory[newState] = updateTime;

        return this with
        {
            CurrentTarget = newTarget,
            State = newState,
            LastStateTime = updateTime,
            TargetHistory = targetHistory,
            StateHistory = stateHistory,
        };
    }

    /// <summary>타겟팅 카운트만 업데이트</summary>
    public ActiveSkillInfo WithTargetingCount(int count, DateTime updateTime)
    {
        return this with { TargetingCount = count, LastStateTime = updateTime };
    }

    /// <summary>스킬 타입만 업데이트 (Casting → Channeling 전환용)</summary>
    public ActiveSkillInfo WithType(SkillType newType, DateTime updateTime)
    {
        return this with { Type = newType, LastStateTime = updateTime };
    }

    /// <summary>IsUsing 플래그만 업데이트</summary>
    public ActiveSkillInfo WithUsing(bool isUsing, DateTime updateTime)
    {
        return this with { IsUsing = isUsing, LastStateTime = updateTime };
    }

    /// <summary>마지막 상태 시간만 업데이트 (채널링 스킬용)</summary>
    public ActiveSkillInfo WithLastTime(DateTime lastTime)
    {
        return this with { LastStateTime = lastTime };
    }

    /// <summary>복합 업데이트 - 상태, IsUsing, 시간 동시 업데이트</summary>
    public ActiveSkillInfo WithStateAndUsing(SkillState newState, bool isUsing, DateTime updateTime)
    {
        var stateHistory = GetStateHistory();
        stateHistory[newState] = updateTime;

        return this with
        {
            State = newState,
            IsUsing = isUsing,
            LastStateTime = updateTime,
            StateHistory = stateHistory,
        };
    }
}

public record InstallationInfo(
    string InstallationId, // UsedBy(설치물 ID)
    string Owner, // Owner (실제 소유자)
    string Target, // Target (공격 대상)
    string SkillName, // SkillName (설치물 스킬 이름)
    DateTime RegisteredAt // RegisteredAt (설치 시간)
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

    // 설치물 정보 관리
    private readonly ConcurrentDictionary<string, List<InstallationInfo>> _installationsByOwner =
        new ConcurrentDictionary<string, List<InstallationInfo>>();
    private readonly ConcurrentDictionary<string, List<InstallationInfo>> _installationsByTarget =
        new ConcurrentDictionary<string, List<InstallationInfo>>();

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
    /// 두 Target 값이 매칭되는지 확인합니다 (유연한 매칭)
    /// </summary>
    private bool IsTargetMatch(string skillTarget, string skillNextTarget, string damageTarget)
    {
        // 정확히 일치하면 매칭
        if (skillTarget == damageTarget || skillNextTarget == damageTarget)
            return true;

        // 둘 중 하나가 특수 값이면 매칭 (광역/전체 공격)
        if (
            skillTarget == "00000000"
            || damageTarget == "00000000"
            || damageTarget == "ffffffff"
            || skillTarget == "ffffffff"
            || skillNextTarget == "00000000"
            || skillNextTarget == "ffffffff"
        )
            return true;

        return false;
    }

    /// <summary>
    /// 기존 IsTargetMatch (하위 호환성)
    /// </summary>
    private bool IsTargetMatch(string skillTarget, string damageTarget)
    {
        return IsTargetMatch(skillTarget, "00000000", damageTarget);
    }

    /// <summary>
    /// 기존 활성 스킬에서 타입을 찾아 반환합니다
    /// </summary>
    private SkillType FindExistingSkillType(string baseSkillName)
    {
        // 1. _castingSkills에서 해당 스킬명으로 활성 중인 스킬 찾기
        var existingSkill = _castingSkills
            .Values.Where(skill => skill.SkillName == baseSkillName)
            .OrderByDescending(skill => skill.LastStateTime)
            .FirstOrDefault();

        if (existingSkill != null)
            return existingSkill.Type;

        return SkillType.Casting;
    }

    private (string baseName, SkillState state, SkillType type) ParseSkillName(string actionName)
    {
        if (actionName.EndsWith("_Casting", StringComparison.OrdinalIgnoreCase))
            return (actionName[..^8], SkillState.Casting, SkillType.Casting);
        if (actionName.EndsWith("_Targeting", StringComparison.OrdinalIgnoreCase))
            return (actionName[..^10], SkillState.Targeting, SkillType.TargetCasting);
        if (actionName.EndsWith("_End", StringComparison.OrdinalIgnoreCase))
        {
            var baseName = actionName[..^4];
            var existingType = FindExistingSkillType(baseName);
            return (baseName, SkillState.Ending, existingType);
        }
        if (actionName.EndsWith("_Hit", StringComparison.OrdinalIgnoreCase))
        {
            var baseName = actionName[..^4];
            var existingType = FindExistingSkillType(baseName);
            return (baseName, SkillState.Hit, existingType);
        }
        if (actionName.Equals("Idle", StringComparison.OrdinalIgnoreCase))
            return (actionName, SkillState.Idle, SkillType.Instant);

        // 기본값: 즉시 스킬
        return (actionName, SkillState.Instant, SkillType.Instant);
    }

    /// <summary>
    /// 스킬 키를 생성합니다 (used_by + startTime)
    /// </summary>
    private static string GetSkillKey(string usedBy, DateTime startTime)
    {
        return $"{usedBy}_{startTime.Ticks}";
    }

    /// <summary>
    /// 활성 스킬 키 찾기
    /// </summary>
    private string? FindActiveSkillKey(string usedBy, string skillName)
    {
        return _castingSkills
            .Where(kvp =>
                kvp.Value.UsedBy == usedBy
                && kvp.Value.SkillName == skillName
                && kvp.Value.State != SkillState.Idle
            )
            .OrderByDescending(kvp => kvp.Value.LastStateTime)
            .FirstOrDefault()
            .Key;
    }

    /// <summary>
    /// 데미지 매칭용 스킬 찾기
    /// </summary>
    private ActiveSkillInfo? FindSkillForDamage(SkillDamagePacket damage, DateTime damageTime)
    {
        var damageTarget = damage.Target;

        // 1. 정확한 타겟 매칭 우선
        var exactMatch = _castingSkills
            .Values.Where(skill =>
                skill.UsedBy == damage.UsedBy
                && IsTargetMatch(skill.CurrentTarget, damageTarget)
                && CanProduceDamage(skill, damageTime)
            )
            .OrderBy(skill => Math.Abs((damageTime - skill.LastStateTime).TotalMilliseconds))
            .FirstOrDefault();

        if (exactMatch != null)
            return exactMatch;

        // 2. 타겟 히스토리 매칭
        var historyMatch = _castingSkills
            .Values.Where(skill =>
                skill.UsedBy == damage.UsedBy
                && skill.TargetHistory!.Any(target => IsTargetMatch(target, damageTarget))
                && CanProduceDamage(skill, damageTime)
            )
            .OrderBy(skill => Math.Abs((damageTime - skill.LastStateTime).TotalMilliseconds))
            .FirstOrDefault();

        return historyMatch;
    }

    /// <summary>
    /// 스킬이 해당 시점에 데미지를 생성할 수 있는지 확인
    /// </summary>
    private bool CanProduceDamage(ActiveSkillInfo skill, DateTime damageTime)
    {
        return skill.State switch
        {
            SkillState.Ending => true, // End 상태에서 데미지 가능
            SkillState.Hit => true, // Hit 상태에서 데미지 가능
            SkillState.Casting when skill.Type == SkillType.Channeling => true, // 채널링 중 데미지
            _ => false,
        };
    }

    /// <summary>
    /// 스킬 완료 처리
    /// </summary>
    private void CompleteSkill(string skillKey, ActiveSkillInfo skill, DateTime completedAt)
    {
        var duration = completedAt - skill.StartTime;

        logger.Info(
            $"[SkillMatcher] Completed skill: {skill.SkillName} "
                + $"Key: {skillKey} Duration: {duration.TotalSeconds:F3}s "
                + $"TargetingCount: {skill.TargetingCount} "
                + $"Targets: [{string.Join(" → ", skill.TargetHistory!)}]"
        );

        _castingSkills.TryRemove(skillKey, out _);
    }

    public void EnqueueSkillAction(SkillActionPacket skillAction, DateTime lastAt)
    {
        var (baseName, state, type) = ParseSkillName(skillAction.ActionName);
        var skillKey = GetSkillKey(baseName, lastAt);

        if (state != SkillState.Instant)
        {
            if (_castingSkills.TryGetValue(skillKey, out var existingSkill))
            {
                UpdateSkillState(skillKey, skillAction, state, lastAt);
            }
            else
            {
                var skillInfo = ActiveSkillInfo.Create(
                    skillAction.UsedBy,
                    skillAction.Target,
                    skillAction.Target,
                    skillAction.NextTarget,
                    baseName,
                    state,
                    type,
                    lastAt,
                    lastAt
                );
                _castingSkills[skillKey] = skillInfo;
                logger.Info($"[SkillMatcher] Started skill: {baseName} Key: {skillKey}");
            }
        }
        else if (state == SkillState.Instant)
        {
            // 즉시 스킬을 _activeSkills에 등록
            if (!_activeSkills.TryGetValue(skillAction.UsedBy, out var userSkills))
            {
                userSkills = new ConcurrentDictionary<DateTime, ActiveSkillInfo>();
                _activeSkills[skillAction.UsedBy] = userSkills;
            }
            var skillInfo = ActiveSkillInfo.Create(
                skillAction.UsedBy,
                skillAction.Target,
                skillAction.Target,
                skillAction.NextTarget,
                baseName,
                state,
                type,
                lastAt,
                lastAt
            );
            userSkills[lastAt] = skillInfo;
            logger.Info(
                $"[SkillMatcher] Started instant skill: {baseName} Key: {skillAction.UsedBy}_{lastAt.Ticks}"
            );
        }
    }

    /// <summary>
    /// 구조적 특성으로 설치물인지 판단 (이름 의존성 제거) ✅
    /// </summary>
    private bool IsInstallationSkill(SkillInfoPacket skillInfo)
    {
        return skillInfo.UsedBy != skillInfo.Owner
            && // 서로 다른 ID
            skillInfo.Owner != "00000000"
            && // 유효한 소유자
            !string.IsNullOrEmpty(skillInfo.Target)
            && // 유효한 대상
            skillInfo.UsedBy.Length == 8; // 설치물 ID 길이 패턴
    }

    /// <summary>
    /// 설치물 정보 등록
    /// </summary>
    private void RegisterInstallation(SkillInfoPacket skillInfo, DateTime lastAt)
    {
        var installation = new InstallationInfo(
            skillInfo.UsedBy, // 설치물 ID
            skillInfo.Owner, // 실제 소유자
            skillInfo.Target, // 공격 대상
            skillInfo.SkillName, // 설치물 스킬명
            lastAt
        );

        // 소유자별 인덱싱 ✅
        if (!_installationsByOwner.TryGetValue(skillInfo.Owner, out var ownerList))
        {
            ownerList = new List<InstallationInfo>();
            _installationsByOwner[skillInfo.Owner] = ownerList;
        }
        ownerList.Add(installation);

        // 대상별 인덱싱 ✅
        if (!_installationsByTarget.TryGetValue(skillInfo.Target, out var targetList))
        {
            targetList = new List<InstallationInfo>();
            _installationsByTarget[skillInfo.Target] = targetList;
        }
        targetList.Add(installation);

        logger.Info(
            $"[SkillMatcher] Registered installation: {skillInfo.SkillName} "
                + $"InstallationId: {skillInfo.UsedBy} Owner: {skillInfo.Owner} Target: {skillInfo.Target}"
        );
    }

    public void EnqueueSkillInfo(SkillInfoPacket skill, DateTime lastAt)
    {
        if (IsInstallationSkill(skill))
        {
            RegisterInstallation(skill, lastAt);
            return;
        }
        var (baseSkillName, state, type) = ParseSkillName(skill.SkillName);

        if (state == SkillState.Idle)
        {
            // Idle 스킬은 중복 등록하지 않고 기존 스킬을 Idle 상태로 전환
            var existingSkillKey = FindActiveSkillKey(skill.UsedBy, baseSkillName);
            if (existingSkillKey != null)
            {
                UpdateSkillState(existingSkillKey, skill, state, lastAt);
                return;
            }

            // 기존 스킬이 없으면 동일 UsedBy의 모든 스킬을 Idle로 전환 시도
            var userSkills = _castingSkills
                .Where(kvp =>
                    kvp.Value.UsedBy == skill.UsedBy && kvp.Value.State != SkillState.Idle
                )
                .ToList();

            if (userSkills.Any())
            {
                // 가장 최근 스킬을 Idle로 전환
                var latestSkill = userSkills
                    .OrderByDescending(kvp => kvp.Value.LastStateTime)
                    .First();
                UpdateSkillState(latestSkill.Key, skill, state, lastAt);
                logger.Info(
                    $"[SkillMatcher] Converted latest skill to Idle: {latestSkill.Value.SkillName}"
                );
                return;
            }

            // 정말 처리할 스킬이 없으면 무시
            logger.Debug($"[SkillMatcher] Ignoring redundant Idle signal from {skill.UsedBy}");
            return;
        }
        if (state == SkillState.Instant)
        {
            if (!_activeSkills.TryGetValue(skill.UsedBy, out var userSkills))
            {
                userSkills = new ConcurrentDictionary<DateTime, ActiveSkillInfo>();
                _activeSkills[skill.UsedBy] = userSkills;
            }

            var skillInfo = ActiveSkillInfo.Create(
                skill.UsedBy,
                skill.Target,
                skill.Target,
                skill.Target,
                baseSkillName,
                state,
                type,
                lastAt,
                lastAt
            );

            userSkills[lastAt] = skillInfo;
            logger.Info(
                $"[SkillMatcher] Started instant skill (Info): {baseSkillName} Key: {skill.UsedBy}_{lastAt.Ticks}"
            );
            return;
        }

        var skillKey = FindActiveSkillKey(skill.UsedBy, baseSkillName);

        if (skillKey != null)
        {
            UpdateSkillState(skillKey, skill, state, lastAt);
        }
        else
        {
            skillKey = GetSkillKey(skill.UsedBy, lastAt);
            var skillInfo = new ActiveSkillInfo(
                skill.UsedBy,
                skill.Target,
                skill.Target,
                skill.Target,
                baseSkillName,
                state,
                type,
                lastAt,
                lastAt,
                TargetHistory: new List<string> { skill.Target },
                StateHistory: new Dictionary<SkillState, DateTime> { [state] = lastAt }
            );
            _castingSkills[skillKey] = skillInfo;
            logger.Info(
                $"[SkillMatcher] Started skill (Info first): {baseSkillName} Key: {skillKey}"
            );
        }
    }

    /// <summary>
    /// 스킬 상태 업데이트
    /// </summary>
    private void UpdateSkillState(
        string skillKey,
        object packet,
        SkillState newState,
        DateTime receivedAt
    )
    {
        if (!_castingSkills.TryGetValue(skillKey, out var existingSkill))
            return;

        var newTarget = packet switch
        {
            SkillActionPacket action => action.Target,
            SkillInfoPacket info => info.Target,
            _ => existingSkill.CurrentTarget,
        };

        var targetingCount = existingSkill.TargetingCount;

        switch (newState)
        {
            case SkillState.Targeting:
                targetingCount++;
                // ✅ 자동 타입 전환: Casting → TargetCasting
                if (existingSkill.Type == SkillType.Casting)
                {
                    existingSkill = existingSkill.WithType(SkillType.TargetCasting, receivedAt);
                    logger.Info(
                        $"[SkillMatcher] Auto-converted skill type: {existingSkill.SkillName} "
                            + $"Casting → TargetCasting (First Targeting detected)"
                    );
                }
                break;

            case SkillState.Hit:
                targetingCount = Math.Max(0, targetingCount - 1);
                if (targetingCount == 0)
                {
                    // 모든 타겟팅 소모 - 스킬 완료
                    CompleteSkill(skillKey, existingSkill, receivedAt);
                    return;
                }
                break;
        }
        var updatedSkill =
            newTarget != existingSkill.CurrentTarget
                ? existingSkill.WithTargetAndState(newTarget, newState, receivedAt)
                : existingSkill.WithState(newState, receivedAt);

        // 타겟 변화 추적
        if (targetingCount != existingSkill.TargetingCount)
        {
            updatedSkill = updatedSkill.WithTargetingCount(targetingCount, receivedAt);
        }

        _castingSkills[skillKey] = updatedSkill;

        logger.Info(
            $"[SkillMatcher] Updated skill {existingSkill.SkillName} "
                + $"Key: {skillKey} State: {newState} Target: {newTarget} "
                + $"TargetingCount: {targetingCount}"
        );
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

        var damageTarget = damagePacket.Target;
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
        // 3. 설치물 스킬 매칭 시도 ✅ (즉시 스킬보다 우선)
        var installationMatch = TryMatchInstallationByData(damagePacket, lastAttackTime);
        if (installationMatch.HasValue)
        {
            logger.Info(
                $"[SkillMatcher] Installation skill matched: [{installationMatch.Value.Key}] {installationMatch.Value.Skill?.SkillName} for damage {damagePacket.Damage}"
            );
            damagePacket.SkillName = installationMatch.Value.Skill?.SkillName ?? "";
            return damagePacket;
        }
        // 4. 활성 스킬 매칭 시도 (기존 활성 스킬에서)
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
        var damageTarget = damagePacket.Target;

        var candidates = source
            .Where(kvp =>
                IsTargetMatch(kvp.Value.CurrentTarget, damageTarget)
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

    private SkillMatchResult? TryMatchInstallationByData(
        SkillDamagePacket damagePacket,
        DateTime lastAttackTime
    )
    {
        InstallationInfo? matchedInstallation = null;

        // 1차 매칭: Owner + Target 기준 ✅
        if (_installationsByOwner.TryGetValue(damagePacket.UsedBy, out var ownerInstallations))
        {
            matchedInstallation = ownerInstallations
                .Where(inst => inst.Target == damagePacket.Target) // 대상도 일치
                .Where(inst =>
                    Math.Abs((lastAttackTime - inst.RegisteredAt).TotalMilliseconds) <= 30000
                )
                .OrderByDescending(inst => inst.RegisteredAt)
                .FirstOrDefault();
        }

        // 2차 매칭: Target 기준으로 확장 검색
        if (
            matchedInstallation == null
            && _installationsByTarget.TryGetValue(damagePacket.Target, out var targetInstallations)
        )
        {
            matchedInstallation = targetInstallations
                .Where(inst => inst.Owner == damagePacket.UsedBy) // 소유자 일치
                .Where(inst =>
                    Math.Abs((lastAttackTime - inst.RegisteredAt).TotalMilliseconds) <= 30000
                )
                .OrderByDescending(inst => inst.RegisteredAt)
                .FirstOrDefault();
        }

        if (matchedInstallation != null)
        {
            logger.Info(
                $"[SkillMatcher] Installation matched by data: {matchedInstallation.SkillName} "
                    + $"InstallationId: {matchedInstallation.InstallationId} "
                    + $"Owner: {matchedInstallation.Owner} Target: {matchedInstallation.Target} "
                    + $"TimeDiff: {Math.Abs((lastAttackTime - matchedInstallation.RegisteredAt).TotalMilliseconds):F0}ms"
            );

            // 가상의 ActiveSkillInfo 생성 (설치물용)
            var skillInfo = ActiveSkillInfo.Create(
                matchedInstallation.Owner, // 실제 소유자
                matchedInstallation.Target, // 대상
                matchedInstallation.Target, // 현재 대상
                "00000000", // NextTarget
                matchedInstallation.SkillName,
                SkillState.Instant,
                SkillType.Installation,
                matchedInstallation.RegisteredAt,
                lastAttackTime
            );

            var skillKey = $"{matchedInstallation.SkillName}_{matchedInstallation.InstallationId}";
            return new SkillMatchResult(skillKey, skillInfo);
        }

        return null;
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
        var damageTarget = damagePacket.Target;

        logger.Debug(
            $"[SkillMatcher] Searching for casting skill candidates with UsedBy: {damagePacket.UsedBy}, Target: {damageTarget}"
        );
        // 1. 채널링 패턴: Casting 상태에서 Damage가 들어오면 Channeling으로 전이
        var channelingCandidate = _castingSkills
            .Where(kvp =>
                kvp.Value.UsedBy == damagePacket.UsedBy
                && kvp.Value.Type == SkillType.Casting
                && kvp.Value.State == SkillState.Casting
                && (
                    IsTargetMatch(kvp.Value.CurrentTarget, kvp.Value.NextTarget, damageTarget)
                    || // NextTarget 매칭 지원! ✅
                    Math.Abs((lastAttackTime - kvp.Value.LastStateTime).TotalMilliseconds) <= 3000
                ) // 시간 기반 매칭
            )
            .OrderBy(kvp => Math.Abs((lastAttackTime - kvp.Value.LastStateTime).TotalMilliseconds))
            .FirstOrDefault();

        if (channelingCandidate.Value != null)
        {
            var updatedSkill = channelingCandidate
                .Value.WithType(SkillType.Channeling, lastAttackTime)
                .WithTarget(damageTarget, lastAttackTime);
            _castingSkills[channelingCandidate.Key] = updatedSkill;
            logger.Info(
                $"[SkillMatcher] Casting skill changed to Channeling: {channelingCandidate.Value.SkillName} "
                    + $"UsedBy: {channelingCandidate.Value.UsedBy} "
                    + $"Target: {channelingCandidate.Value.CurrentTarget} → {damageTarget} "
                    + $"NextTarget: {channelingCandidate.Value.NextTarget} "
                    + // ✅ NextTarget 로깅 추가
                    $"TimeDiff: {Math.Abs((lastAttackTime - channelingCandidate.Value.LastStateTime).TotalMilliseconds):F0}ms"
            );
            // 채널링으로 전환되었으므로 즉시 채널링 매칭 결과 반환 ✅
            return new SkillMatchResult(channelingCandidate.Key, updatedSkill);
        }
        // 2. 타게팅 캐스팅 패턴: TargetCasting + Ending 상태에서만 매칭
        var targetingCandidate = _castingSkills
            .Where(kvp =>
                IsTargetMatch(kvp.Value.CurrentTarget, damageTarget)
                && kvp.Value.UsedBy == damagePacket.UsedBy
                && !kvp.Value.IsUsing
                && kvp.Value.Type == SkillType.TargetCasting
                && (kvp.Value.State == SkillState.Targeting || kvp.Value.State == SkillState.Ending)
            )
            .OrderBy(kvp => Math.Abs((lastAttackTime - kvp.Value.LastStateTime).TotalMilliseconds))
            .FirstOrDefault();

        if (targetingCandidate.Value != null)
        {
            _castingSkills[targetingCandidate.Key] = targetingCandidate.Value.WithUsing(
                true,
                lastAttackTime
            );
            logger.Info(
                $"[SkillMatcher] TargetCasting skill matched: {targetingCandidate.Value.SkillName} "
                    + $"UsedBy: {targetingCandidate.Value.UsedBy} Target: {targetingCandidate.Value.CurrentTarget} "
                    + $"State: {targetingCandidate.Value.State} TargetingCount: {targetingCandidate.Value.TargetingCount}"
            );
            return new SkillMatchResult(targetingCandidate.Key, targetingCandidate.Value);
        }
        // 3. Lazy Casting 매칭 + 지연 정리 (수정됨!) ✅
        var lazyCastingCandidates = _castingSkills
            .Where(kvp =>
                kvp.Value.UsedBy == damagePacket.UsedBy
                && kvp.Value.Type == SkillType.Casting
                && kvp.Value.State == SkillState.Ending
                && !kvp.Value.IsUsing
            )
            .ToList();

        foreach (var candidate in lazyCastingCandidates)
        {
            var timeDiff = Math.Abs(
                (lastAttackTime - candidate.Value.LastStateTime).TotalMilliseconds
            );
            // A. 타겟 매칭되는 스킬이 있다면 매칭 시도
            if (IsTargetMatch(candidate.Value.CurrentTarget, damageTarget))
            {
                // Lazy Casting은 더 긴 시간 허용 (지연된 데미지 패턴)
                if (timeDiff <= 5000) // 5초 이내 매칭 허용
                {
                    _castingSkills[candidate.Key] = candidate.Value.WithUsing(true, lastAttackTime);
                    logger.Info(
                        $"[SkillMatcher] Lazy Casting skill matched: {candidate.Value.SkillName} "
                            + $"UsedBy: {candidate.Value.UsedBy} Target: {candidate.Value.CurrentTarget} "
                            + $"NextTarget: {candidate.Value.NextTarget} "
                            + // ✅ NextTarget 로깅
                            $"TimeDiff: {timeDiff:F0}ms"
                    );
                    return new SkillMatchResult(candidate.Key, candidate.Value);
                }
            }
            // B. 매칭되지 않거나 시간 초과된 스킬들 정리
            if (timeDiff > 5000) // 5초 후 정리 (지연 데미지 고려)
            {
                _castingSkills.TryRemove(candidate.Key, out _);
                logger.Info(
                    $"[SkillMatcher] Cleaned up expired lazy casting skill: {candidate.Value.SkillName} "
                        + $"(End: {candidate.Value.LastStateTime:HH:mm:ss.fff}, TimeDiff: {timeDiff:F0}ms)"
                );
            }
            else if (timeDiff > 1000) // 1초 후부터 대기 상태 로깅
            {
                logger.Debug(
                    $"[SkillMatcher] Lazy casting skill waiting for damage: {candidate.Value.SkillName} "
                        + $"(TimeDiff: {timeDiff:F0}ms)"
                );
            }
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
                    _castingSkills[key] = skill.WithLastTime(lastAttackTime);
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
                if (skill.CurrentTarget != "ffffffff")
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
                $"[SkillMatcher] Removed {castingSkill!.SkillName} UsagedBy: {castingSkill.UsedBy} Target: {castingSkill.CurrentTarget}"
            );
        }
    }
}
