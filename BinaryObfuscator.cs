using System.Diagnostics;
using System.Runtime.InteropServices;
using NLog;

namespace PacketCapture;

/// <summary>
/// 바이너리 노이즈 및 파일 조작을 통한 보안/난독화 기능을 제공하는 클래스
/// </summary>
public static class BinaryObfuscator
{
    private static readonly Random random = new Random();
    private static readonly HashSet<string> _tempFiles = [];
    private static readonly object _lockObject = new();
    private static bool _cleanupRegistered = false;
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    /// <summary>
    /// 프로세스 종료 시 임시 파일 정리를 위한 정리 핸들러 등록
    /// </summary>
    public static void RegisterCleanup()
    {
        lock (_lockObject)
        {
            if (_cleanupRegistered)
                return;

            // 콘솔 종료 핸들러 등록 (Windows)
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Console.CancelKeyPress += OnProcessExit;
            }

            // AppDomain 종료 핸들러 등록 (모든 플랫폼)
            AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

            _cleanupRegistered = true;
            Logger.Info("Temporary file cleanup handlers registered");
        }
    }

    /// <summary>
    /// 임시 파일을 등록하여 추적 목록에 추가
    /// </summary>
    /// <param name="filePath">추적할 임시 파일 경로</param>
    public static void RegisterTempFile(string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
            return;

        lock (_lockObject)
        {
            _tempFiles.Add(filePath);
            Logger.Debug($"Registered temp file: {filePath}");
        }
    }

    /// <summary>
    /// 프로세스 종료 시 호출되는 정리 메서드
    /// </summary>
    private static void OnProcessExit(object? sender, EventArgs e)
    {
        CleanupTempFiles();
    }

    /// <summary>
    /// 처리되지 않은 예외 발생 시 호출되는 정리 메서드
    /// </summary>
    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        CleanupTempFiles();
    }

    /// <summary>
    /// 등록된 모든 임시 파일을 삭제
    /// </summary>
    public static void CleanupTempFiles()
    {
        lock (_lockObject)
        {
            if (_tempFiles.Count == 0)
                return;

            Logger.Info($"Cleaning up {_tempFiles.Count} temporary files");

            foreach (var tempFile in _tempFiles.ToList())
            {
                try
                {
                    if (File.Exists(tempFile))
                    {
                        File.Delete(tempFile);
                        Logger.Debug($"Deleted temp file: {tempFile}");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warn(ex, $"Failed to delete temp file: {tempFile}");
                }
            }

            _tempFiles.Clear();
            Logger.Info("Temporary file cleanup completed");
        }
    }

    /// <summary>
    /// 바이너리 데이터에 랜덤 노이즈를 추가하여 난독화
    /// </summary>
    /// <param name="data">원본 바이너리 데이터</param>
    /// <param name="noiseLevel">노이즈 레벨 (0.0 ~ 1.0)</param>
    /// <returns>노이즈가 추가된 바이너리 데이터</returns>
    public static byte[] AddBinaryNoise(ReadOnlySpan<byte> data, double noiseLevel = 0.1)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(noiseLevel);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(noiseLevel, 1.0);

        var result = data.ToArray();
        var noiseCount = (int)(data.Length * noiseLevel);

        for (int i = 0; i < noiseCount; i++)
        {
            var index = random.Next(result.Length);
            result[index] ^= (byte)random.Next(256);
        }

        return result;
    }

    /// <summary>
    /// 파일에 더미 데이터를 추가하여 크기 및 내용 변조
    /// </summary>
    /// <param name="filePath">대상 파일 경로</param>
    /// <param name="dummySize">추가할 더미 데이터 크기 (바이트)</param>
    /// <returns>성공 여부</returns>
    public static async Task<bool> AddDummyDataAsync(string filePath, int dummySize = 1024)
    {
        try
        {
            ArgumentException.ThrowIfNullOrEmpty(filePath);
            ArgumentOutOfRangeException.ThrowIfNegative(dummySize);

            if (!File.Exists(filePath))
                return false;

            var dummyData = new byte[dummySize];
            random.NextBytes(dummyData);

            await File.AppendAllTextAsync(
                filePath,
                $"\n// Dummy data: {Convert.ToBase64String(dummyData)}"
            );

            Logger.Debug($"Added {dummySize} bytes of dummy data to {filePath}");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, $"Failed to add dummy data to file: {filePath}");
            return false;
        }
    }

    /// <summary>
    /// 임시 파일을 생성하고 자동 추적 등록
    /// </summary>
    /// <param name="prefix">파일명 접두사</param>
    /// <param name="extension">파일 확장자</param>
    /// <returns>생성된 임시 파일 경로</returns>
    public static string CreateTempFile(string prefix = "temp", string extension = ".tmp")
    {
        RegisterCleanup(); // 정리 핸들러가 등록되지 않았다면 등록

        var tempPath = Path.Combine(Path.GetTempPath(), $"{prefix}_{Guid.NewGuid():N}{extension}");

        RegisterTempFile(tempPath);
        return tempPath;
    }

    /// <summary>
    /// 파일 타임스탬프를 랜덤하게 변조
    /// </summary>
    /// <param name="filePath">대상 파일 경로</param>
    /// <returns>성공 여부</returns>
    public static bool RandomizeFileTimestamp(string filePath)
    {
        try
        {
            ArgumentException.ThrowIfNullOrEmpty(filePath);

            if (!File.Exists(filePath))
                return false;

            var randomDate = DateTime.UtcNow.AddDays(-random.Next(1, 365));

            File.SetCreationTime(filePath, randomDate);
            File.SetLastWriteTime(filePath, randomDate.AddHours(random.Next(1, 24)));
            File.SetLastAccessTime(filePath, randomDate.AddDays(random.Next(1, 30)));

            Logger.Debug($"Randomized timestamp for file: {filePath}");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, $"Failed to randomize timestamp for file: {filePath}");
            return false;
        }
    }

    /// <summary>
    /// 프로세스 메모리에 더미 데이터를 할당하여 메모리 덤프 방어
    /// </summary>
    /// <param name="sizeInMB">할당할 메모리 크기 (MB)</param>
    /// <returns>할당된 더미 데이터 참조 (GC 방지용)</returns>
    public static List<byte[]> AllocateDummyMemory(int sizeInMB = 10)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sizeInMB);

        var dummyArrays = new List<byte[]>();
        const int chunkSize = 1024 * 1024; // 1MB chunks

        for (int i = 0; i < sizeInMB; i++)
        {
            var chunk = new byte[chunkSize];
            random.NextBytes(chunk);
            dummyArrays.Add(chunk);
        }

        Logger.Debug($"Allocated {sizeInMB}MB of dummy memory");
        return dummyArrays;
    }

    /// <summary>
    /// 현재 프로세스의 우선순위를 낮춰 디버깅 방해
    /// </summary>
    public static void LowerProcessPriority()
    {
        try
        {
            using var currentProcess = Process.GetCurrentProcess();
            currentProcess.PriorityClass = ProcessPriorityClass.BelowNormal;
            Logger.Debug("Lowered process priority");
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "Failed to lower process priority");
        }
    }

    /// <summary>
    /// 등록된 임시 파일 수를 반환
    /// </summary>
    public static int TempFileCount
    {
        get
        {
            lock (_lockObject)
            {
                return _tempFiles.Count;
            }
        }
    }

    /// <summary>
    /// 랜덤한 파일명을 생성합니다.
    /// </summary>
    /// <param name="extension">파일 확장자 (기본값: .exe)</param>
    /// <returns>랜덤 파일명</returns>
    public static string GenerateRandomFilename(string extension = ".exe")
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        var result = new char[10];
        for (int i = 0; i < 10; i++)
        {
            result[i] = chars[random.Next(chars.Length)];
        }
        return new string(result) + extension;
    }

    /// <summary>
    /// 바이너리 파일에 랜덤 노이즈를 추가하여 해시값을 변경합니다.
    /// </summary>
    /// <param name="filePath">변경할 파일 경로</param>
    public static void MutateBinary(string filePath)
    {
        try
        {
            int noiseLength = random.Next(1, 17); // 1-16바이트 랜덤 노이즈
            byte[] noise = new byte[noiseLength];
            random.NextBytes(noise);

            using var fs = new FileStream(filePath, FileMode.Append, FileAccess.Write);
            fs.Write(noise, 0, noise.Length);
        }
        catch (Exception ex)
        {
            Logger.Debug($"Binary mutation failed: {ex.Message}");
        }
    }

    /// <summary>
    /// 콘솔 타이틀을 주기적으로 랜덤하게 변경합니다.
    /// </summary>
    /// <param name="cancellationToken">취소 토큰</param>
    public static void StartRandomTitleChanger(CancellationToken cancellationToken)
    {
        Task.Run(
            async () =>
            {
                const string chars =
                    "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        var titleChars = new char[12];
                        for (int i = 0; i < 12; i++)
                        {
                            titleChars[i] = chars[random.Next(chars.Length)];
                        }
                        string randomTitle = new string(titleChars);

                        if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                        {
                            SetConsoleTitle(randomTitle);
                        }

                        await Task.Delay(100, cancellationToken);
                    }
                    catch (Exception)
                    {
                        // 무시하고 계속 진행
                    }
                }
            },
            cancellationToken
        );
    }

    /// <summary>
    /// 원본 실행 파일을 랜덤한 이름으로 복사하고 노이즈를 추가한 후 실행합니다.
    /// 생성된 임시 파일은 프로세스 종료 시 자동으로 삭제됩니다.
    /// </summary>
    /// <param name="originalPath">원본 파일 경로</param>
    /// <returns>성공 여부</returns>
    public static bool TryCreateObfuscatedCopy(string originalPath)
    {
        try
        {
            RegisterCleanup(); // 정리 핸들러 등록 확인

            string currentDir =
                Path.GetDirectoryName(originalPath) ?? Directory.GetCurrentDirectory();
            string newName = GenerateRandomFilename();
            string newPath = Path.Combine(currentDir, newName);

            // 원본 파일을 새 이름으로 복사
            File.Copy(originalPath, newPath);

            // 임시 파일로 등록 (프로세스 종료 시 자동 삭제)
            RegisterTempFile(newPath);

            // 복사된 파일에 노이즈 추가
            MutateBinary(newPath);

            // 새로운 프로세스로 시작 (원본 경로를 인자로 전달)
            var startInfo = new ProcessStartInfo
            {
                FileName = newPath,
                Arguments = $"\"{originalPath}\"",
                UseShellExecute = false,
                CreateNoWindow = false,
            };

            Process.Start(startInfo);

            Logger.Info($"Created obfuscated copy: {newPath} (will be cleaned up on exit)");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, $"Failed to create obfuscated copy: {ex.Message}");
            return false;
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleTitle(string lpConsoleTitle);
}
