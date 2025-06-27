# VMware에서 호스트 패킷 캡처 설정 가이드

## VMware 네트워크 설정

### 1. VMware 네트워크 어댑터 설정

VMware에서 호스트의 패킷을 캡처하려면 다음과 같이 설정해야 합니다:

#### 방법 1: Bridged Network 사용
```
1. VM Settings → Network Adapter
2. Network connection: Bridged (Connected directly to the physical network)
3. Configure Adapters... → 호스트의 실제 네트워크 어댑터 선택
4. Replicate physical network connection state 체크
```

#### 방법 2: Host-only Network 사용
```
1. VM Settings → Network Adapter
2. Network connection: Host-only
3. VMware → Edit → Virtual Network Editor
4. VMnet1 (Host-only) 선택
5. Promiscuous mode: Allow (또는 Allow VMs)
```

### 2. Promiscuous Mode 활성화

VMware에서 패킷 캡처를 위해 Promiscuous Mode를 활성화해야 합니다:

#### VMware Workstation/Player:
```
1. Edit → Virtual Network Editor (관리자 권한 필요)
2. 사용할 네트워크 선택 (VMnet0, VMnet1 등)
3. Advanced → Promiscuous mode → Allow 선택
```

#### VMware vSphere:
```
1. vSphere Client에서 VM 선택
2. Configure → Hardware → Network adapters
3. Edit → Security → Promiscuous mode → Accept
```

### 3. Windows 가상머신 설정

Windows 가상머신에서 패킷 캡처를 위한 추가 설정:

#### 방화벽 설정:
```powershell
# 방화벽에서 Raw Socket 허용
netsh advfirewall firewall add rule name="Packet Capture" dir=in action=allow protocol=any
```

#### 관리자 권한:
```
프로그램을 반드시 관리자 권한으로 실행해야 합니다.
```

## 네트워크 토폴로지별 설정

### 시나리오 1: 호스트와 VM이 같은 네트워크
```
Host: 192.168.1.100
VM:   192.168.1.101
→ Bridged Network 사용, 직접 패킷 캡처 가능
```

### 시나리오 2: Host-only 네트워크
```
Host: 192.168.137.1 (VMnet1)
VM:   192.168.137.2 (VMnet1)
→ Host-only Network 사용, VMware의 가상 스위치를 통한 캡처
```

### 시나리오 3: NAT 네트워크
```
Host: 192.168.1.100
VM:   192.168.88.100 (NAT)
→ 직접적인 호스트 패킷 캡처 불가, TCP 프록시 방식 사용
```

## 문제 해결

### 1. "No VMware network interfaces found" 오류
```
- VM 네트워크 어댑터가 활성화되어 있는지 확인
- VMware Tools가 설치되어 있는지 확인
- 네트워크 어댑터 드라이버 재설치
```

### 2. 패킷이 캡처되지 않는 경우
```
- Promiscuous mode가 활성화되어 있는지 확인
- 관리자 권한으로 실행했는지 확인
- 방화벽이 차단하고 있지 않은지 확인
- 올바른 네트워크 인터페이스를 선택했는지 확인
```

### 3. 권한 문제
```
- 프로그램을 관리자 권한으로 실행
- VMware를 관리자 권한으로 실행
- Virtual Network Editor에서 설정 변경시 관리자 권한 필요
```

## 권장 설정

### 개발/테스트 환경:
```
- Network: Host-only
- Promiscuous mode: Allow
- VMware Tools: 최신 버전 설치
```

### 프로덕션 환경:
```
- Network: Bridged (보안 검토 후)
- Promiscuous mode: Allow VMs only
- 네트워크 분리 고려
```
