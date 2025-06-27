using System.Net;
using System.Net.NetworkInformation;
using NLog;
using PacketDotNet;
using SharpPcap;
using SharpPcap.LibPcap;

namespace PacketCapture
{
    /// <summary>
    /// Npcap 기반 고성능 패킷 캡처 클래스
    /// 최신 C# 기능과 크로스 플랫폼 지원을 제공합니다.
    /// </summary>
    public sealed class NpcapPacketCapture
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();
        private readonly int targetPort;
        private readonly Action<byte[], DateTime> onPacketReceived;
        private readonly List<ICaptureDevice> captureDevices = [];
        private bool isCapturing;

        /// <summary>
        /// NpcapPacketCapture 인스턴스를 초기화합니다.
        /// </summary>
        /// <param name="port">캡처할 대상 포트</param>
        /// <param name="packetHandler">패킷 수신 시 호출될 핸들러</param>
        /// <exception cref="ArgumentNullException">packetHandler가 null인 경우</exception>
        /// <exception cref="ArgumentOutOfRangeException">port가 유효하지 않은 경우</exception>
        public NpcapPacketCapture(int port, Action<byte[], DateTime> packetHandler)
        {
            if (port is <= 0 or > 65535)
                throw new ArgumentOutOfRangeException(
                    nameof(port),
                    "포트는 1-65535 범위여야 합니다."
                );

            targetPort = port;
            onPacketReceived =
                packetHandler ?? throw new ArgumentNullException(nameof(packetHandler));
        }

        /// <summary>
        /// Npcap이 사용 가능한지 확인합니다.
        /// </summary>
        /// <returns>Npcap 사용 가능 여부</returns>
        public static bool IsNpcapAvailable()
        {
            try
            {
                var devices = CaptureDeviceList.Instance;
                return devices.Count > 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 패킷 캡처를 시작합니다.
        /// </summary>
        /// <exception cref="InvalidOperationException">캡처 장치를 찾을 수 없는 경우</exception>
        public void StartCapture()
        {
            var devices = CaptureDeviceList.Instance;
            if (devices.Count == 0)
            {
                throw new InvalidOperationException(
                    "No capture devices found. Please ensure Npcap is installed."
                );
            }

            // 모든 활성 네트워크 인터페이스를 찾아서 캡처
            var selectedDevices = FindAllActiveNetworkInterfaces(devices);

            if (selectedDevices.Count == 0)
            {
                throw new InvalidOperationException(
                    "Could not find any suitable network interfaces for packet capture"
                );
            }

            logger.Info($"Starting capture on {selectedDevices.Count} device(s):");
            selectedDevices.ForEach(device => logger.Info($"  - {device.Description}"));

            try
            {
                // 모든 선택된 디바이스에서 캡처 시작
                foreach (var device in selectedDevices)
                {
                    ConfigureAndStartDevice(device);
                    captureDevices.Add(device);
                }

                // 모든 디바이스에서 캡처 시작
                isCapturing = true;
                captureDevices.ForEach(device => device.StartCapture());

                logger.Info($"Npcap packet capture started on {captureDevices.Count} device(s)");
            }
            catch (Exception ex)
            {
                logger.Error($"Failed to start capture: {ex.Message}");
                StopCapture(); // 이미 열린 디바이스들 정리
                throw;
            }
        }

        /// <summary>
        /// 개별 디바이스를 구성하고 시작합니다.
        /// </summary>
        /// <param name="device">구성할 캡처 디바이스</param>
        private void ConfigureAndStartDevice(ICaptureDevice device)
        {
            // 디바이스 열기
            device.Open(DeviceModes.Promiscuous, 1000);

            // 포트 필터 설정 (양방향 트래픽)
            string filter = $"tcp port {targetPort}";
            device.Filter = filter;
            logger.Info($"Capture filter set on {device.Description}: {filter}");

            // 패킷 이벤트 핸들러 설정
            device.OnPacketArrival += OnPacketArrival;
        }

        /// <summary>
        /// 패킷 캡처를 중지합니다.
        /// </summary>
        public void StopCapture()
        {
            if (!isCapturing || captureDevices.Count == 0)
                return;

            try
            {
                isCapturing = false;

                foreach (var device in captureDevices)
                {
                    StopSingleDevice(device);
                }

                captureDevices.Clear();
                logger.Info("All packet capture devices stopped");
            }
            catch (Exception ex)
            {
                logger.Error($"Error stopping capture: {ex.Message}");
            }
        }

        /// <summary>
        /// 개별 디바이스의 캡처를 중지합니다.
        /// </summary>
        /// <param name="device">중지할 디바이스</param>
        private void StopSingleDevice(ICaptureDevice device)
        {
            try
            {
                device.StopCapture();
                device.Close();
                logger.Info($"Stopped capture on device: {device.Description}");
            }
            catch (Exception ex)
            {
                logger.Error(
                    $"Error stopping capture on device {device.Description}: {ex.Message}"
                );
            }
        }

        /// <summary>
        /// 모든 활성 네트워크 인터페이스를 찾습니다.
        /// </summary>
        /// <param name="devices">사용 가능한 캡처 디바이스 목록</param>
        /// <returns>우선순위가 정렬된 활성 디바이스 목록</returns>
        private List<ICaptureDevice> FindAllActiveNetworkInterfaces(CaptureDeviceList devices)
        {
            logger.Info("Searching for all active network interfaces...");

            var platform = Environment.OSVersion.Platform;
            logger.Info($"Detected platform: {platform}"); // 1단계: 활성화된 네트워크 인터페이스 찾기
            var activeDevices = devices
                .Cast<ICaptureDevice>()
                .Where(device =>
                    !IsVirtualInterface(device.Description.ToLower(), device.Name.ToLower())
                )
                .Where(IsActiveDevice)
                .ToList();

            activeDevices.ForEach(device =>
                logger.Info($"Found active device: {device.Description}")
            );

            // 2단계: 우선순위별로 정렬 (이더넷 우선, Wi-Fi 포함)
            var prioritizedDevices = PrioritizeNetworkDevices(activeDevices); // 3단계: 디바이스가 없는 경우 대안 찾기
            if (prioritizedDevices.Count == 0)
            {
                logger.Warn("No ideal devices found, searching for fallback devices");
                prioritizedDevices = devices
                    .Cast<ICaptureDevice>()
                    .Where(device =>
                        !IsVirtualInterface(device.Description.ToLower(), device.Name.ToLower())
                    )
                    .ToList();

                prioritizedDevices.ForEach(device =>
                    logger.Info($"Added fallback device: {device.Description}")
                );
            }

            return prioritizedDevices;
        }

        /// <summary>
        /// 네트워크 디바이스의 우선순위를 정렬합니다.
        /// </summary>
        /// <param name="activeDevices">활성 디바이스 목록</param>
        /// <returns>우선순위가 정렬된 디바이스 목록</returns>
        private List<ICaptureDevice> PrioritizeNetworkDevices(List<ICaptureDevice> activeDevices)
        {
            logger.Info("Prioritizing network devices...");

            List<ICaptureDevice> prioritizedList = [];
            var ethernetDevices = activeDevices.Where(IsWiredEthernetAdapter).ToList();
            var wifiDevices = activeDevices.Where(d => IsWiFiAdapter(d.Description)).ToList();
            var otherDevices = activeDevices.Except(ethernetDevices).Except(wifiDevices).ToList();

            // 1순위: 기본 게이트웨이를 사용하는 이더넷 장치
            if (
                FindDeviceByDefaultGateway(ethernetDevices) is var defaultGatewayDevice
                && defaultGatewayDevice != null
            )
            {
                prioritizedList.Add(defaultGatewayDevice);
                ethernetDevices.Remove(defaultGatewayDevice);
                logger.Info(
                    $"Added default gateway ethernet device: {defaultGatewayDevice.Description}"
                );
            }

            // 2순위: 나머지 이더넷 장치들 (우선순위별 정렬)
            var sortedEthernet = ethernetDevices
                .OrderByDescending(d => GetEthernetAdapterPriority(d.Description))
                .ToList();

            prioritizedList.AddRange(sortedEthernet);
            sortedEthernet.ForEach(device =>
                logger.Info(
                    $"Added ethernet device: {device.Description} "
                        + $"(priority: {GetEthernetAdapterPriority(device.Description)})"
                )
            );

            // 3순위: Wi-Fi 장치들 처리
            HandleWiFiDevices(prioritizedList, ethernetDevices, wifiDevices);

            // 5순위: 기타 네트워크 장치들
            prioritizedList.AddRange(otherDevices);
            otherDevices.ForEach(device =>
                logger.Info($"Added other network device: {device.Description}")
            );

            logger.Info($"Device prioritization complete. Total devices: {prioritizedList.Count}");
            return prioritizedList;
        }

        /// <summary>
        /// Wi-Fi 디바이스들을 처리합니다.
        /// </summary>
        private void HandleWiFiDevices(
            List<ICaptureDevice> prioritizedList,
            List<ICaptureDevice> ethernetDevices,
            List<ICaptureDevice> wifiDevices
        )
        {
            // 이더넷이 없는 경우에만 Wi-Fi를 메인으로 사용
            if (ethernetDevices.Count == 0 && prioritizedList.Count == 0)
            {
                if (
                    FindDeviceByDefaultGateway(wifiDevices) is var defaultWifiDevice
                    && defaultWifiDevice != null
                )
                {
                    prioritizedList.Add(defaultWifiDevice);
                    wifiDevices.Remove(defaultWifiDevice);
                    logger.Info(
                        $"Added default gateway Wi-Fi device (no ethernet found): {defaultWifiDevice.Description}"
                    );
                }

                prioritizedList.AddRange(wifiDevices);
                wifiDevices.ForEach(device =>
                    logger.Info($"Added Wi-Fi device (no ethernet found): {device.Description}")
                );
            }
            else if (ethernetDevices.Count == 0 && prioritizedList.Count > 0)
            {
                // 이더넷이 있지만 기본 게이트웨이가 없는 경우, Wi-Fi도 포함
                prioritizedList.AddRange(wifiDevices);
                wifiDevices.ForEach(device =>
                    logger.Info($"Added Wi-Fi device (supplementary): {device.Description}")
                );
            }
        }

        /// <summary>
        /// 가상 인터페이스인지 확인합니다.
        /// </summary>
        /// <param name="description">디바이스 설명</param>
        /// <param name="name">디바이스 이름</param>
        /// <returns>가상 인터페이스 여부</returns>
        private static bool IsVirtualInterface(string description, string name)
        {
            string[] virtualKeywords =
            [
                "wan miniport",
                "loopback",
                "bluetooth",
                "vmware",
                "hyper-v",
                "nordvpn",
                "openvpn",
                "virtual",
                "tunnel",
                "tap",
                "tun",
                "docker",
                "veth",
                "bridge",
                "dummy",
            ];

            return virtualKeywords.Any(keyword =>
                description.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                || name.Contains(keyword, StringComparison.OrdinalIgnoreCase)
            );
        }

        /// <summary>
        /// 디바이스가 활성 상태인지 확인합니다.
        /// </summary>
        /// <param name="device">확인할 디바이스</param>
        /// <returns>활성 상태 여부</returns>
        private static bool IsActiveDevice(ICaptureDevice device)
        {
            try
            {
                return device is LibPcapLiveDevice liveDevice
                    ? liveDevice.Interface.Addresses.Count > 0
                    : true; // 주소 정보를 확인할 수 없는 경우 활성으로 간주
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 디바이스의 친숙한 이름을 가져옵니다.
        /// </summary>
        /// <param name="device">디바이스</param>
        /// <returns>친숙한 이름</returns>
        private static string GetFriendlyName(ICaptureDevice device)
        {
            try
            {
                return device is LibPcapLiveDevice liveDevice
                    ? liveDevice.Interface.FriendlyName ?? device.Description
                    : device.Description;
            }
            catch
            {
                return device.Description;
            }
        }

        /// <summary>
        /// 패킷 도착 시 호출되는 이벤트 핸들러입니다.
        /// </summary>
        /// <param name="sender">이벤트 발생자</param>
        /// <param name="e">패킷 캡처 이벤트 인수</param>
        private void OnPacketArrival(object sender, SharpPcap.PacketCapture e)
        {
            try
            {
                var packet = PacketDotNet.Packet.ParsePacket(
                    e.GetPacket().LinkLayerType,
                    e.GetPacket().Data
                );

                if (packet.Extract<TcpPacket>() is not TcpPacket tcpPacket)
                    return;

                // 포트 필터링 (src port 또는 dst port가 타겟 포트인 경우)
                if (tcpPacket.SourcePort != targetPort && tcpPacket.DestinationPort != targetPort)
                    return;

                // TCP 페이로드 추출
                var payload = tcpPacket.PayloadData;
                if (payload?.Length > 0)
                {
                    logger.Debug(
                        $"Captured TCP packet: {tcpPacket.SourcePort} -> {tcpPacket.DestinationPort}, "
                            + $"seq: {tcpPacket.SequenceNumber}, payload size: {payload.Length}"
                    );

                    // Python capture.py의 packet_processor와 동일한 방식으로 처리
                    // SEQ 번호와 페이로드, 타임스탬프를 콜백으로 전달
                    onPacketReceived(payload, DateTime.UtcNow);
                }
            }
            catch (Exception ex)
            {
                logger.Error($"Error processing captured packet: {ex.Message}");
            }
        }

        /// <summary>
        /// 사용 가능한 네트워크 디바이스 목록을 출력합니다.
        /// </summary>
        public static void PrintAvailableDevices()
        {
            logger.Info("=== Available Network Devices ===");

            try
            {
                var devices = CaptureDeviceList.Instance;
                for (int i = 0; i < devices.Count; i++)
                {
                    var device = devices[i];
                    logger.Info($"[{i}] {device.Name}");
                    logger.Info($"    Description: {device.Description}");

                    if (
                        device is LibPcapLiveDevice liveDevice
                        && liveDevice.Interface.Addresses.Count > 0
                    )
                    {
                        liveDevice
                            .Interface.Addresses.ToList()
                            .ForEach(addr => logger.Info($"    Address: {addr.Addr}"));
                    }
                    logger.Info("");
                }
            }
            catch (Exception ex)
            {
                logger.Error($"Error listing devices: {ex.Message}");
            }
        }

        /// <summary>
        /// 유선 이더넷 어댑터인지 확인합니다.
        /// </summary>
        /// <param name="device">확인할 디바이스</param>
        /// <returns>유선 이더넷 어댑터 여부</returns>
        private bool IsWiredEthernetAdapter(ICaptureDevice device) =>
            IsWiredEthernetAdapter(device.Description);

        /// <summary>
        /// 유선 이더넷 어댑터인지 확인합니다.
        /// </summary>
        /// <param name="description">디바이스 설명</param>
        /// <returns>유선 이더넷 어댑터 여부</returns>
        private bool IsWiredEthernetAdapter(string description)
        {
            var desc = description.ToLower();

            // Wi-Fi 어댑터는 확실히 제외
            if (IsWiFiAdapter(description))
            {
                logger.Debug($"Excluding Wi-Fi adapter: {description}");
                return false;
            }

            // 유선 이더넷 키워드 검사
            string[] wiredKeywords =
            [
                "ethernet",
                "marvell",
                "realtek",
                "aquantia",
                "aqtion",
                "ethernet controller",
                "network adapter",
                "lan controller",
                "gigabit ethernet",
                "fast ethernet",
                "intel",
            ];

            var isWired = wiredKeywords.Any(keyword =>
                desc.Contains(keyword, StringComparison.OrdinalIgnoreCase)
            );

            if (isWired)
            {
                logger.Debug($"Identified as wired ethernet: {description}");
            }

            return isWired;
        }

        /// <summary>
        /// Wi-Fi 어댑터인지 확인합니다.
        /// </summary>
        /// <param name="description">디바이스 설명</param>
        /// <returns>Wi-Fi 어댑터 여부</returns>
        private static bool IsWiFiAdapter(string description)
        {
            var desc = description.ToLower();

            // Wi-Fi 관련 키워드들 (더 포괄적으로)
            string[] wifiKeywords =
            [
                "wi-fi",
                "wifi",
                "wireless",
                "802.11",
                "ax",
                "ac",
                "dual band",
                "tri-band",
                "wireless network",
                "wireless lan",
                "wlan",
                "wireless adapter",
            ];

            var isWifi = wifiKeywords.Any(keyword =>
                desc.Contains(keyword, StringComparison.OrdinalIgnoreCase)
            );

            if (isWifi)
            {
                logger.Debug($"Identified as Wi-Fi adapter: {description}");
            }

            return isWifi;
        }

        /// <summary>
        /// 이더넷 어댑터의 우선순위를 계산합니다.
        /// </summary>
        /// <param name="description">어댑터 설명</param>
        /// <returns>우선순위 점수 (높을수록 우선순위 높음)</returns>
        private static int GetEthernetAdapterPriority(string description)
        {
            var desc = description.ToLower();

            return desc switch
            {
                var d
                    when d.Contains("marvell") || d.Contains("aquantia") || d.Contains("aqtion") =>
                    LogAndReturn(100, "Marvell/Aquantia adapter", description),
                var d when d.Contains("realtek") => LogAndReturn(
                    80,
                    "Realtek adapter",
                    description
                ),
                var d when d.Contains("intel") && d.Contains("ethernet controller") => LogAndReturn(
                    60,
                    "Intel Ethernet Controller",
                    description
                ),
                var d when d.Contains("ethernet") => LogAndReturn(
                    40,
                    "Generic ethernet adapter",
                    description
                ),
                _ => LogAndReturn(0, "Unknown adapter", description),
            };

            static int LogAndReturn(int priority, string type, string description)
            {
                logger.Debug($"{type} priority: {priority} - {description}");
                return priority;
            }
        }

        private ICaptureDevice? FindDeviceByDefaultGateway(List<ICaptureDevice> devices)
        {
            try
            {
                // 현재 기본 게이트웨이를 가진 네트워크 인터페이스 찾기
                var networkInterfaces = NetworkInterface
                    .GetAllNetworkInterfaces()
                    .Where(ni => ni.OperationalStatus == OperationalStatus.Up)
                    .ToList();

                foreach (var ni in networkInterfaces)
                {
                    var ipProps = ni.GetIPProperties();
                    var gateways = ipProps.GatewayAddresses;

                    // 기본 게이트웨이가 있는 인터페이스인지 확인
                    if (gateways.Count > 0 && gateways.Any(g => !g.Address.Equals(IPAddress.Any)))
                    {
                        // 해당 인터페이스의 MAC 주소로 캡처 디바이스 찾기
                        var macAddress = ni.GetPhysicalAddress();
                        if (macAddress != null && macAddress.GetAddressBytes().Length > 0)
                        {
                            foreach (var device in devices)
                            {
                                if (device is LibPcapLiveDevice liveDevice)
                                {
                                    // MAC 주소 또는 인터페이스 이름으로 매칭
                                    if (
                                        liveDevice.Interface.MacAddress != null
                                        && liveDevice.Interface.MacAddress.Equals(macAddress)
                                    )
                                    {
                                        logger.Info(
                                            $"Matched device by MAC address: {device.Description}"
                                        );
                                        return device;
                                    }

                                    // 인터페이스 이름으로도 시도
                                    if (
                                        liveDevice.Interface.FriendlyName?.Contains(ni.Description)
                                            == true
                                        || ni.Description.Contains(
                                            liveDevice.Interface.FriendlyName ?? ""
                                        )
                                    )
                                    {
                                        logger.Info(
                                            $"Matched device by interface name: {device.Description}"
                                        );
                                        return device;
                                    }
                                }
                            }
                        }

                        // 기본 게이트웨이 정보 로깅
                        foreach (var gateway in gateways)
                        {
                            logger.Debug(
                                $"Interface {ni.Description} has gateway: {gateway.Address}"
                            );
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Debug($"Error finding device by default gateway: {ex.Message}");
            }

            return null;
        }
    }
}
