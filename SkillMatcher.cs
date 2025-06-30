using System.Collections.Generic;
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
        UserData.RegisterType<SkillStatePacket>();
        UserData.RegisterType<ChangeHpPacket>();
        UserData.RegisterType<SkillActionPacket>();
        UserData.RegisterType<SkillInfoPacket>();
        UserData.RegisterType<FlagBits>();
        UserData.RegisterType<DamageModel>();

        // 로깅 함수 등록
        _luaScript.Globals["Log"] = (Action<string, string>)LogFromLua;

        // 시간 함수 등록
        _luaScript.Globals["GetCurrentTime"] =
            (Func<double>)(() => DateTime.UtcNow.Ticks / 10000000.0);

        // 이벤트 콜백 등록
        _luaScript.Globals["OnDamageMatchedCallback"] = (Action<DamageModel>)OnDamageMatchedFromLua;

        // DamageModel 생성 함수 등록
        _luaScript.Globals["CreateDamageModel"] =
            (Func<string, string, int, string, byte[],int, DamageModel>)
                CreateDamageModel;
    }

    private void LoadScript()
    {
        try
        {
            if (File.Exists(_scriptPath))
            {
                var scriptContent = File.ReadAllText(_scriptPath);
                _luaScript.DoString(scriptContent);
                logger.Info($"[LuaEngine] Script loaded successfully: {_scriptPath}");
            }
            else
            {
                logger.Warn($"[LuaEngine] Script file not found: {_scriptPath}");
            }
        }
        catch (ScriptRuntimeException ex)
        {
            logger.Error(
                $"[LuaEngine] Lua Runtime Error loading script {_scriptPath}: {ex.DecoratedMessage}"
            );
            logger.Error($"[LuaEngine] Lua Stack Trace: {ex.CallStack}");
        }
        catch (SyntaxErrorException ex)
        {
            logger.Error(
                $"[LuaEngine] Lua Syntax Error in script {_scriptPath}: {ex.DecoratedMessage}"
            );
        }
        catch (Exception ex)
        {
            logger.Error(
                ex,
                $"[LuaEngine] Unexpected Error loading script {_scriptPath}: {ex.Message}"
            );
        }
    }

    private void SetupFileWatcher()
    {
        var directory = Path.GetDirectoryName(_scriptPath) ?? "";
        var fileName = Path.GetFileName(_scriptPath);

        _scriptWatcher = new FileSystemWatcher(directory, fileName);
        _scriptWatcher.Changed += (sender, e) =>
        {
            try
            {
                Thread.Sleep(100); // 파일 쓰기 완료 대기
                logger.Info("[LuaEngine] Script file changed, reloading...");
                LoadScript();
                logger.Info("[LuaEngine] Script reload completed successfully");
            }
            catch (Exception ex)
            {
                logger.Error(ex, "[LuaEngine] Error during script auto-reload");
            }
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
        catch (ScriptRuntimeException ex)
        {
            logger.Error(
                $"[LuaEngine] Lua Runtime Error in EnqueueSkillAction: {ex.DecoratedMessage}"
            );
            logger.Error($"[LuaEngine] Lua Stack Trace: {ex.CallStack}");
        }
        catch (Exception ex)
        {
            logger.Error(ex, $"[LuaEngine] Unexpected Error in EnqueueSkillAction: {ex.Message}");
        }
    }

    /// <summary>
    /// Lua에서 HP 정보 처리
    /// </summary>
    public void EnqueueHpChange(ChangeHpPacket hpInfo, DateTime lastAt)
    {
        try
        {
            _luaScript.Call(
                _luaScript.Globals["EnqueueHpChange"],
                hpInfo,
                lastAt.Ticks / 10000000.0
            );
        }
        catch (ScriptRuntimeException ex)
        {
            logger.Error(
                $"[LuaEngine] Lua Runtime Error in EnqueueHpChange: {ex.DecoratedMessage}"
            );
            logger.Error($"[LuaEngine] Lua Stack Trace: {ex.CallStack}");
        }
        catch (Exception ex)
        {
            logger.Error(ex, $"[LuaEngine] Unexpected Error in EnqueueHpChange: {ex.Message}");
        }
    }

    /// <summary>
    /// Lua에서 스킬 상태 처리
    /// </summary>
    /// <param name="skillState"></param>
    /// <param name="lastAt"></param>
    public void EnqueueSkillState(SkillStatePacket skillState, DateTime lastAt)
    {
        try
        {
            _luaScript.Call(
                _luaScript.Globals["EnqueueSkillState"],
                skillState,
                lastAt.Ticks / 10000000.0
            );
        }
        catch (ScriptRuntimeException ex)
        {
            logger.Error(
                $"[LuaEngine] Lua Runtime Error in EnqueueSkillState: {ex.DecoratedMessage}"
            );
            logger.Error($"[LuaEngine] Lua Stack Trace: {ex.CallStack}");
        }
        catch (Exception ex)
        {
            logger.Error(ex, $"[LuaEngine] Unexpected Error in EnqueueSkillState: {ex.Message}");
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
        catch (ScriptRuntimeException ex)
        {
            logger.Error(
                $"[LuaEngine] Lua Runtime Error in EnqueueSkillInfo: {ex.DecoratedMessage}"
            );
            logger.Error($"[LuaEngine] Lua Stack Trace: {ex.CallStack}");
        }
        catch (Exception ex)
        {
            logger.Error(ex, $"[LuaEngine] Unexpected Error in EnqueueSkillInfo: {ex.Message}");
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
        catch (ScriptRuntimeException ex)
        {
            logger.Error($"[LuaEngine] Lua Runtime Error in EnqueueDamage: {ex.DecoratedMessage}");
            logger.Error($"[LuaEngine] Lua Stack Trace: {ex.CallStack}");
        }
        catch (Exception ex)
        {
            logger.Error(ex, $"[LuaEngine] Unexpected Error in EnqueueDamage: {ex.Message}");
        }
    }

    /// <summary>
    /// 데미지 매칭 이벤트 핸들러
    /// </summary>
    public event Action<DamageModel>? OnDamageMatched;

    private void OnDamageMatchedFromLua(DamageModel model)
    {
        OnDamageMatched?.Invoke(model);
    }

    /// <summary>
    /// Lua에서 플래그와 함께 DamageModel을 생성하기 위한 헬퍼 함수
    /// </summary>
    private DamageModel CreateDamageModel(
        string usedBy,
        string target,
        int damage,
        string skillName,
        byte[] flagBytes,
        int selfTarget
    )
    {
        var flagBits = new FlagBits();
        if (flagBytes.Length >= 4)
        {
            flagBits = FlagBits.ParseFlags(flagBytes);
        }

        return new DamageModel
        {
            UsedBy = usedBy,
            Target = target,
            Damage = damage,
            SkillName = skillName,
            Flags = flagBits,
            FlagBytes = flagBytes,
            SelfTarget = selfTarget
        };
    }

    public void Dispose()
    {
        _scriptWatcher?.Dispose();
    }
}
