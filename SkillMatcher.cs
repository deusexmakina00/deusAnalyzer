using MoonSharp.Interpreter;
using NLog;

namespace PacketCapture;

/// <summary>
/// 완전한 Lua 기반 SkillMatcher 엔진
/// </summary>
public sealed class SkillMatcher
{
    private static readonly Logger logger = LogManager.GetCurrentClassLogger();
    private readonly Script _luaScript;
    private FileSystemWatcher? _scriptWatcher;
    private string _scriptPath;

    public SkillMatcher(string scriptPath = "scripts/skill_matcher.lua")
    {
        _scriptPath = scriptPath;
        _luaScript = new Script();

        // C# 타입 등록
        RegisterTypes();

        // 스크립트 로드
        LoadScript();

        // 파일 감시자 설정 (실시간 리로딩)
        SetupFileWatcher();
    }

    private void RegisterTypes()
    {
        // 패킷 타입들 등록
        UserData.RegisterType<SkillDamagePacket>();
        UserData.RegisterType<SkillActionPacket>();
        UserData.RegisterType<SkillInfoPacket>();

        // 로깅 함수 등록
        _luaScript.Globals["Log"] = (Action<string, string>)LogFromLua;

        // 시간 함수 등록
        _luaScript.Globals["GetCurrentTime"] =
            (Func<double>)(() => DateTime.UtcNow.Ticks / 10000000.0);

        // 이벤트 콜백 등록
        _luaScript.Globals["OnDamageMatchedCallback"] =
            (Action<SkillDamagePacket>)OnDamageMatchedFromLua;
    }

    private void LoadScript()
    {
        try
        {
            if (File.Exists(_scriptPath))
            {
                var scriptContent = File.ReadAllText(_scriptPath);
                _luaScript.DoString(scriptContent);
                logger.Info($"[LuaEngine] Script loaded: {_scriptPath}");
            }
            else
            {
                logger.Warn($"[LuaEngine] Script file not found: {_scriptPath}");
            }
        }
        catch (Exception ex)
        {
            logger.Error(ex, $"[LuaEngine] Failed to load script: {_scriptPath}");
        }
    }

    private void SetupFileWatcher()
    {
        var directory = Path.GetDirectoryName(_scriptPath) ?? "";
        var fileName = Path.GetFileName(_scriptPath);

        _scriptWatcher = new FileSystemWatcher(directory, fileName);
        _scriptWatcher.Changed += (sender, e) =>
        {
            Thread.Sleep(100); // 파일 쓰기 완료 대기
            logger.Info("[LuaEngine] Script file changed, reloading...");
            LoadScript();
        };
        _scriptWatcher.EnableRaisingEvents = true;
    }

    private void LogFromLua(string level, string message)
    {
        switch (level.ToUpper())
        {
            case "DEBUG":
                logger.Debug(message);
                break;
            case "INFO":
                logger.Info(message);
                break;
            case "WARN":
                logger.Warn(message);
                break;
            case "ERROR":
                logger.Error(message);
                break;
            default:
                logger.Info(message);
                break;
        }
    }

    /// <summary>
    /// Lua에서 데미지 매칭 수행
    /// </summary>
    public SkillDamagePacket MatchDamageToSkill(
        SkillDamagePacket damagePacket,
        DateTime lastAttackTime
    )
    {
        try
        {
            var result = _luaScript.Call(
                _luaScript.Globals["MatchDamageToSkill"],
                damagePacket,
                lastAttackTime.Ticks / 10000000.0
            );

            // Lua 결과를 C# 객체로 변환
            return result.ToObject<SkillDamagePacket>() ?? damagePacket;
        }
        catch (Exception ex)
        {
            logger.Error(ex, "[LuaEngine] Error in MatchDamageToSkill");
            return damagePacket; // 실패 시 원본 반환
        }
    }

    /// <summary>
    /// Lua에서 스킬 액션 처리
    /// </summary>
    public void EnqueueSkillAction(SkillActionPacket skillAction, DateTime lastAt)
    {
        try
        {
            _luaScript.Call(
                _luaScript.Globals["EnqueueSkillAction"],
                skillAction,
                lastAt.Ticks / 10000000.0
            );
        }
        catch (Exception ex)
        {
            logger.Error(ex, "[LuaEngine] Error in EnqueueSkillAction");
        }
    }

    /// <summary>
    /// Lua에서 스킬 정보 처리
    /// </summary>
    public void EnqueueSkillInfo(SkillInfoPacket skillInfo, DateTime lastAt)
    {
        try
        {
            _luaScript.Call(
                _luaScript.Globals["EnqueueSkillInfo"],
                skillInfo,
                lastAt.Ticks / 10000000.0
            );
        }
        catch (Exception ex)
        {
            logger.Error(ex, "[LuaEngine] Error in EnqueueSkillInfo");
        }
    }

    /// <summary>
    /// Lua에서 스킬 정보 처리
    /// </summary>
    public void EnqueueDamage(SkillDamagePacket damage, DateTime lastAt)
    {
        try
        {
            _luaScript.Call(_luaScript.Globals["EnqueueDamage"], damage, lastAt.Ticks / 10000000.0);
        }
        catch (Exception ex)
        {
            logger.Error(ex, "[LuaEngine] Error in EnqueueDamage");
        }
    }

    /// <summary>
    /// Lua에서 오래된 스킬 정리
    /// </summary>
    public void CleanupOldSkills(DateTime lastAt)
    {
        try
        {
            _luaScript.Call(_luaScript.Globals["CleanupOldSkills"], lastAt.Ticks / 10000000.0);
        }
        catch (Exception ex)
        {
            logger.Error(ex, "[LuaEngine] Error in CleanupOldSkills");
        }
    }

    /// <summary>
    /// 데미지 매칭 이벤트 핸들러
    /// </summary>
    public event Action<SkillDamagePacket>? OnDamageMatched;

    /// <summary>
    /// Lua에서 데미지 매칭 완료 시 호출
    /// </summary>
    private void OnDamageMatchedFromLua(SkillDamagePacket damage)
    {
        OnDamageMatched?.Invoke(damage);
    }

    public void Dispose()
    {
        _scriptWatcher?.Dispose();
    }
}
