// ========== 데미지 미터 애플리케이션 ==========
class DamageMeterApp {
    constructor() {
        this.ws = null;
        this.wsUrl = `ws://${window.location.hostname}:9001`;  // C# WebSocket 서버 포트
        this.isConnected = false;
          // 데이터 저장소
        this.damageData = new Map(); // userKey -> { name, totalDamage, skills: Map, hits, crits, addHits }
        this.targetData = new Map(); // targetName -> data
        this.logData = [];
        this.sessionStartTime = null;
        this.selectedTarget = null;
        this.selectedUser = null;          // UI 캐시 및 상태 추적
        this.lastLogCount = 0;
        this.lastLogId = null; // 마지막 로그 ID 추적
        this.lastSkillBarHash = '';
        this.lastRankingHash = '';
        
        // 가상 스크롤 설정
        this.virtualScroll = {
            itemHeight: 60, // 각 로그 아이템 높이 (px)
            visibleCount: 50, // 한 번에 렌더링할 아이템 수
            scrollTop: 0,
            totalHeight: 0,
            startIndex: 0,
            endIndex: 0
        };
        
        // UI 요소들
        this.elements = {};
        
        // 필터 설정
        this.filters = {
            skillFilter: '',
            filterDot: false,
            autoReset: true
        };
        
        // 통계 데이터
        this.statistics = {
            totalHits: 0,
            totalCrits: 0,
            totalAddHits: 0,
            totalDamage: 0
        };
        // 스킬 매핑 데이터 (translation.js 사용)
        this.skillMappings = window.SKILL_MAPPINGS || window.DATA || {};
        
        // 프로필 관리
        this.profiles = new Map();
        this.currentProfile = null;
        this.compareProfile = null;
        this.isComparisonMode = false;
        
        this.init();
    }

    // ========== 초기화 ==========
    init() {        this.initElements();
        this.initEventListeners();
        this.initWebSocket();
        this.loadTheme();
        this.loadProfiles();
        this.startUpdateLoop();
    }

    initElements() {
        // 헤더 요소들
        this.elements.wsStatus = document.getElementById('ws-status');
        this.elements.themeToggle = document.getElementById('theme-toggle');
        
        // 전투 정보 요소들
        this.elements.battleTime = document.getElementById('battle-time');
        this.elements.totalDamage = document.getElementById('total-damage');
        this.elements.totalDps = document.getElementById('total-dps');
        this.elements.sessionStatus = document.getElementById('session-status');
        
        // 필터 요소들
        this.elements.skillFilter = document.getElementById('skill-filter');
        this.elements.filterDot = document.getElementById('filter-dot');
        this.elements.autoReset = document.getElementById('auto-reset');
        
        // 선택 요소들
        this.elements.selectedTargetDisplay = document.getElementById('selected-target-display');
        this.elements.selectedUserDisplay = document.getElementById('selected-user-display');
        this.elements.targetList = document.getElementById('target-list');
        this.elements.userList = document.getElementById('user-list');
        
        // 탭 요소들
        this.elements.tabBtns = document.querySelectorAll('.tab-btn');
        this.elements.tabContents = document.querySelectorAll('.tab-content');
        
        // 콘텐츠 요소들
        this.elements.rankingList = document.getElementById('ranking-list');
        this.elements.logContainer = document.getElementById('log-container');
        this.elements.skillBarsContainer = document.getElementById('skill-bars-container');
        
        // 통계 요소들
        this.elements.totalHits = document.getElementById('total-hits');
        this.elements.totalCrits = document.getElementById('total-crits');
        this.elements.totalAddHits = document.getElementById('total-add-hits');
        this.elements.avgDamage = document.getElementById('avg-damage');
        this.elements.critRate = document.getElementById('crit-rate');
        this.elements.addHitRate = document.getElementById('add-hit-rate');
          // 파일 입력
        this.elements.fileInput = document.getElementById('file-input');
        
        // 프로필 관리 요소들
        this.elements.profileName = document.getElementById('profile-name');
        this.elements.saveProfileBtn = document.getElementById('save-profile-btn');
        this.elements.profileList = document.getElementById('profile-list');
        this.elements.compareProfileSelect = document.getElementById('compare-profile-select');
        this.elements.compareToggle = document.getElementById('compare-toggle');
    }    initEventListeners() {
        // 테마 토글
        if (this.elements.themeToggle) {
            this.elements.themeToggle.addEventListener('click', () => this.toggleTheme());
            // 모바일 터치 지원
            this.elements.themeToggle.addEventListener('touchend', (e) => {
                e.preventDefault();
                this.toggleTheme();
            });
        }
        
        // 필터 이벤트
        if (this.elements.skillFilter) {
            this.elements.skillFilter.addEventListener('input', () => this.updateFilter());
        }
        if (this.elements.filterDot) {
            this.elements.filterDot.addEventListener('change', () => this.updateFilter());
        }
        if (this.elements.autoReset) {
            this.elements.autoReset.addEventListener('change', () => this.updateFilter());
        }
        
        // 탭 이벤트 (모바일 터치 지원)
        this.elements.tabBtns.forEach(btn => {
            btn.addEventListener('click', () => this.switchTab(btn.dataset.tab));
            btn.addEventListener('touchend', (e) => {
                e.preventDefault();
                this.switchTab(btn.dataset.tab);
            });
        });
          // 파일 입력 이벤트
        if (this.elements.fileInput) {
            this.elements.fileInput.addEventListener('change', (e) => this.handleFileLoad(e));
        }
        
        // 프로필 관리 이벤트
        if (this.elements.saveProfileBtn) {
            this.elements.saveProfileBtn.addEventListener('click', () => this.saveProfile());
        }
        if (this.elements.compareToggle) {
            this.elements.compareToggle.addEventListener('change', () => this.toggleComparison());
        }
        
        // 키보드 단축키
        document.addEventListener('keydown', (e) => this.handleKeyboard(e));
        
        // 모바일 방향 변화 감지
        window.addEventListener('orientationchange', () => {
            setTimeout(() => this.handleOrientationChange(), 200);
        });
        
        // 모바일 뷰포트 크기 변화 감지
        window.addEventListener('resize', () => {
            clearTimeout(this.resizeTimer);
            this.resizeTimer = setTimeout(() => this.handleResize(), 150);
        });
        
        // 터치 스크롤 개선을 위한 passive 이벤트
        document.addEventListener('touchstart', this.handleTouchStart.bind(this), { passive: true });
        document.addEventListener('touchmove', this.handleTouchMove.bind(this), { passive: true });
    }
    
    handleTouchStart(e) {
        this.touchStartY = e.touches[0].clientY;
    }
    
    handleTouchMove(e) {
        // 부드러운 스크롤을 위한 처리
        if (e.target.closest('.log-container, .ranking-container, .skill-bars-container, .selection-list-container')) {
            // 스크롤 가능한 컨테이너 내부에서는 기본 동작 허용
            return;
        }
    }
    
    handleOrientationChange() {
        // 방향 변화 시 레이아웃 재계산
        this.updateUI();
        console.log('화면 방향 변경됨');
    }
    
    handleResize() {
        // 창 크기 변화 시 UI 업데이트
        this.updateUI();
    }

    getSkillDisplayName(skillName) {
        if (!this.skillMappings || !skillName) return skillName;
        return this.skillMappings[skillName] || skillName;
    }

    // ========== WebSocket 연결 ==========
    initWebSocket() {
        this.connectWebSocket();
    }

    connectWebSocket() {
        try {
            this.ws = new WebSocket(this.wsUrl);
            
            this.ws.onopen = () => {
                console.log('WebSocket 연결됨');
                this.isConnected = true;
                this.updateConnectionStatus('🟢 연결됨');
            };
            
            this.ws.onmessage = (event) => {
                this.handleWebSocketMessage(event);
            };
            
            this.ws.onclose = () => {
                console.log('WebSocket 연결 종료');
                this.isConnected = false;
                this.updateConnectionStatus('🔴 연결 끊김');
                
                // 5초 후 재연결 시도
                setTimeout(() => this.connectWebSocket(), 5000);
            };
            
            this.ws.onerror = (error) => {
                console.error('WebSocket 오류:', error);
                this.updateConnectionStatus('🟡 오류');
            };
            
        } catch (error) {
            console.error('WebSocket 연결 실패:', error);
            this.updateConnectionStatus('🔴 연결 실패');
            
            // 5초 후 재연결 시도
            setTimeout(() => this.connectWebSocket(), 5000);
        }
    }

    handleWebSocketMessage(event) {
        try {
            const rawData = event.data;
            
            // 파이프로 구분된 데이터 파싱
            const parts = rawData.split('|');
            if (parts.length >= 24) {
                const data = this.parseMessageData(parts);
                this.processDamageData(data);
            } else {
                console.log('잘못된 메시지 형식:', rawData);
            }
        } catch (error) {
            console.error('메시지 파싱 오류:', error);
        }
    }

    parseMessageData(parts) {
        // capture.py의 to_log() 형식에 맞춰 파싱
        return {
            timestamp: parseInt(parts[0]),
            user_name: parts[1],
            target_name: parts[2],
            skill_name: parts[3],
            damage: parseInt(parts[4]),
            is_crit: parts[5] === '1',
            is_add_hit: parts[6] === '1',
            is_unguarded: parts[7] === '1',
            is_break: parts[8] === '1',
            is_first_hit: parts[9] === '1',
            is_default_attack: parts[10] === '1',
            is_multi_attack: parts[11] === '1',
            is_power: parts[12] === '1',
            is_fast: parts[13] === '1',
            is_dot: parts[14] === '1',
            is_ice: parts[15] === '1',
            is_fire: parts[16] === '1',
            is_electric: parts[17] === '1',
            is_holy: parts[18] === '1',
            is_dark: parts[19] === '1',
            is_bleed: parts[20] === '1',
            is_poison: parts[21] === '1',
            is_mind: parts[22] === '1',
            skill_id: parseInt(parts[23])
        };
    }

    updateConnectionStatus(status) {
        if (this.elements.wsStatus) {
            this.elements.wsStatus.textContent = status;
        }
    }

    // ========== 데이터 처리 ==========
    processDamageData(data) {
        const { 
            user_name, 
            target_name, 
            skill_name, 
            damage, 
            is_crit, 
            is_add_hit,
            is_unguarded,
            is_break,
            is_first_hit,
            is_default_attack,
            is_multi_attack,
            is_power,
            is_fast,
            is_dot,
            is_ice,
            is_fire,
            is_electric,
            is_holy,
            is_dark,
            is_bleed,
            is_poison,
            is_mind,
            skill_id,
            timestamp 
        } = data;

        // 세션 시작 시간 자동 설정
        if (!this.sessionStartTime) {
            this.sessionStartTime = Date.now();
            this.updateSessionStatus('🟢 진행중');
        }

        // 사용자 데이터 업데이트
        const userKey = user_name;
        if (!this.damageData.has(userKey)) {
            this.damageData.set(userKey, {
                name: user_name,
                totalDamage: 0,
                skills: new Map(),
                hits: 0,
                crits: 0,
                addHits: 0,
                dps: 0
            });
        }

        const userData = this.damageData.get(userKey);
        userData.totalDamage += damage;
        
        if (!is_add_hit) {
            userData.hits++;
        } else {
            userData.addHits++;
        }
        
        if (is_crit) userData.crits++;        // 스킬 데이터 업데이트
        const displaySkillName = this.getSkillDisplayName(skill_name);
        if (!userData.skills.has(skill_name)) {
            userData.skills.set(skill_name, {
                name: skill_name,
                displayName: displaySkillName,
                damage: 0,
                hits: 0,
                crits: 0,
                addHits: 0,
                lastDamage: 0,
                lastHitTime: 0,
                minDamage: Infinity,
                maxDamage: 0
            });
        }

        const skillData = userData.skills.get(skill_name);
        skillData.damage += damage;
        skillData.lastDamage = damage; // 마지막 데미지 업데이트
        skillData.lastHitTime = timestamp || Date.now(); // 마지막 히트 시간 업데이트
        
        // 최소, 최대 데미지 업데이트
        if (damage < skillData.minDamage) {
            skillData.minDamage = damage;
        }
        if (damage > skillData.maxDamage) {
            skillData.maxDamage = damage;
        }
        
        if (!is_add_hit) {
            skillData.hits++;
        } else {
            skillData.addHits++;
        }
        
        if (is_crit) skillData.crits++;

        // 타겟 데이터 업데이트
        if (!this.targetData.has(target_name)) {
            this.targetData.set(target_name, {
                name: target_name,
                totalDamage: 0,
                hits: 0
            });
        }

        const targetData = this.targetData.get(target_name);
        targetData.totalDamage += damage;
        targetData.hits++;        // 로그 데이터 추가
        this.addLogEntry({
            timestamp: timestamp || Date.now(),
            user_name,
            target_name,
            skill_name,
            damage,
            is_crit,
            is_add_hit,
            is_unguarded,
            is_break,
            is_first_hit,
            is_default_attack,
            is_multi_attack,
            is_power,
            is_fast,
            is_dot,
            is_ice,
            is_fire,
            is_electric,
            is_holy,
            is_dark,
            is_bleed,
            is_poison,
            is_mind,
            skill_id
        });

        // 통계 업데이트
        this.updateStatistics();
    }

    addLogEntry(logEntry) {
        this.logData.unshift(logEntry);
        
        // 로그 최대 1000개로 제한
        if (this.logData.length > 1000) {
            this.logData = this.logData.slice(0, 1000);
        }
    }

    updateStatistics() {
        this.statistics.totalHits = 0;
        this.statistics.totalCrits = 0;
        this.statistics.totalAddHits = 0;
        this.statistics.totalDamage = 0;

        for (const userData of this.damageData.values()) {
            this.statistics.totalHits += userData.hits;
            this.statistics.totalCrits += userData.crits;
            this.statistics.totalAddHits += userData.addHits;
            this.statistics.totalDamage += userData.totalDamage;
        }
    }

    calculateDPS() {
        if (!this.sessionStartTime) return;
        
        const elapsed = (Date.now() - this.sessionStartTime) / 1000;
        if (elapsed <= 0) return;

        for (const userData of this.damageData.values()) {
            userData.dps = Math.round(userData.totalDamage / elapsed);
        }
    }

    updateSessionStatus(status) {
        if (this.elements.sessionStatus) {
            this.elements.sessionStatus.textContent = status;
        }
    }

    // ========== UI 업데이트 ==========
    updateUI() {
        this.updateBattleInfo();        this.updateRanking();
        this.updateStatisticsDisplay();
        this.updateSkillBars();
        this.updateLogs();
        this.updateSelectionLists();
        this.updateProfileList();
    }

    updateBattleInfo() {
        if (!this.elements.battleTime) return;

        // 전투 시간
        const elapsed = this.sessionStartTime ? 
            Math.floor((Date.now() - this.sessionStartTime) / 1000) : 0;
        this.elements.battleTime.textContent = `${elapsed}초`;

        // 총 데미지
        this.elements.totalDamage.textContent = this.formatNumber(this.statistics.totalDamage);

        // 총 DPS
        const totalDps = elapsed > 0 ? Math.round(this.statistics.totalDamage / elapsed) : 0;
        this.elements.totalDps.textContent = this.formatNumber(totalDps);
    }

    updateRanking() {
        if (!this.elements.rankingList) return;

        const filteredUsers = this.getFilteredUsers();
        
        if (filteredUsers.length === 0) {
            this.elements.rankingList.innerHTML = '<div class="no-data-message">랭킹 데이터를 기다리는 중...</div>';
            return;
        }

        // 데미지 순으로 정렬
        const sortedUsers = Array.from(filteredUsers.values())
            .sort((a, b) => b.totalDamage - a.totalDamage);

        let html = '';
        sortedUsers.forEach((user, index) => {
            const rank = index + 1;
            const rankClass = rank <= 3 ? `rank-${rank}` : '';
            const selectedClass = this.selectedUser === user.name ? 'selected' : '';
            
            html += `
                <div class="ranking-item ${selectedClass}" onclick="app.selectUser('${user.name}')">
                    <div class="ranking-rank ${rankClass}">${rank}</div>
                    <div class="ranking-info">
                        <div class="ranking-name">${user.name}</div>
                        <div class="ranking-stats">
                            <span>데미지: ${this.formatNumber(user.totalDamage)}</span>
                            <span>DPS: ${this.formatNumber(user.dps)}</span>
                            <span>타격: ${user.hits}회</span>
                            <span>크리: ${user.crits}회</span>
                        </div>
                    </div>
                </div>
            `;
        });

        this.elements.rankingList.innerHTML = html;
    }

    updateStatisticsDisplay() {
        if (!this.elements.totalHits) return;

        this.elements.totalHits.textContent = this.formatNumber(this.statistics.totalHits);
        
        const avgDamage = this.statistics.totalHits > 0 ? 
            Math.round(this.statistics.totalDamage / this.statistics.totalHits) : 0;
        this.elements.avgDamage.textContent = this.formatNumber(avgDamage);
        
        this.elements.totalCrits.textContent = this.formatNumber(this.statistics.totalCrits);
        
        const critRate = this.statistics.totalHits > 0 ? 
            ((this.statistics.totalCrits / this.statistics.totalHits) * 100).toFixed(1) : '0.0';
        this.elements.critRate.textContent = `${critRate}%`;
        
        this.elements.totalAddHits.textContent = this.formatNumber(this.statistics.totalAddHits);
        
        const addHitRate = this.statistics.totalHits > 0 ? 
            ((this.statistics.totalAddHits / this.statistics.totalHits) * 100).toFixed(1) : '0.0';
        this.elements.addHitRate.textContent = `${addHitRate}%`;
    }    updateSkillBars() {
        if (!this.elements.skillBarsContainer) return;

        const skillMap = new Map();
        const filteredUsers = this.getFilteredUsers();        // 모든 사용자의 스킬 데이터 합계
        for (const userData of filteredUsers.values()) {
            for (const [skillName, skillData] of userData.skills) {
                if (!skillMap.has(skillName)) {
                    skillMap.set(skillName, { ...skillData });
                } else {
                    const existing = skillMap.get(skillName);
                    existing.damage += skillData.damage;
                    existing.hits += skillData.hits;
                    existing.crits += skillData.crits;
                    existing.addHits += skillData.addHits;
                    
                    // 최소, 최대 데미지 업데이트
                    if (skillData.minDamage < existing.minDamage) {
                        existing.minDamage = skillData.minDamage;
                    }
                    if (skillData.maxDamage > existing.maxDamage) {
                        existing.maxDamage = skillData.maxDamage;
                    }
                    
                    // 더 최근의 마지막 데미지와 시간 사용
                    if (skillData.lastHitTime > (existing.lastHitTime || 0)) {
                        existing.lastDamage = skillData.lastDamage;
                        existing.lastHitTime = skillData.lastHitTime;
                    }
                }
            }
        }

        if (skillMap.size === 0) {
            this.elements.skillBarsContainer.innerHTML = '<div class="no-data-message">스킬 데이터를 기다리는 중...</div>';
            return;
        }        // 데미지 순으로 정렬
        const sortedSkills = Array.from(skillMap.values())
            .sort((a, b) => b.damage - a.damage)
            .slice(0, 20); // 상위 20개만 표시

        // 전체 데미지 계산 (지분 계산용)
        const totalDamage = sortedSkills.reduce((sum, skill) => sum + skill.damage, 0);
        const maxDamage = Math.max(...sortedSkills.map(s => s.damage));

        let html = '';
        sortedSkills.forEach(skill => {
            const barPercentage = maxDamage > 0 ? (skill.damage / maxDamage * 100) : 0;
            const damageShare = totalDamage > 0 ? ((skill.damage / totalDamage) * 100) : 0;
            const avgDamage = skill.hits > 0 ? Math.round(skill.damage / skill.hits) : 0;
            const critRate = skill.hits > 0 ? Math.round((skill.crits / skill.hits) * 100) : 0;
            const addHitRate = skill.hits > 0 ? Math.round((skill.addHits / skill.hits) * 100) : 0;
              html += `
                <div class="skill-bar">
                    <div class="skill-bar-header">
                        <span class="skill-name">${skill.displayName || skill.name}</span>
                        <div class="skill-damage-info">
                            <span class="skill-damage">${this.formatNumber(skill.damage, false)}</span>
                            <span class="skill-last-damage">[${this.formatNumber(skill.lastDamage || 0, false)}]</span>
                            <span class="skill-share">(${damageShare.toFixed(1)}%)</span>
                        </div>
                    </div>
                    <div class="skill-bar-bg">
                        <div class="skill-bar-fill" style="width: ${barPercentage}%"></div>
                        <div class="skill-share-text">${damageShare.toFixed(1)}%</div>
                    </div>                    <div class="skill-stats">
                        <div class="skill-stat-item">
                            <span class="skill-stat-label">타격수</span>
                            <span class="skill-stat-value">${skill.hits}회</span>
                        </div>
                        <div class="skill-stat-item">
                            <span class="skill-stat-label">평균 데미지</span>
                            <span class="skill-stat-value">${this.formatNumber(avgDamage, false)}</span>
                        </div>
                        <div class="skill-stat-item">
                            <span class="skill-stat-label">최소/최대</span>
                            <span class="skill-stat-value">${this.formatNumber(skill.minDamage === Infinity ? 0 : skill.minDamage, false)} / ${this.formatNumber(skill.maxDamage, false)}</span>
                        </div>
                        <div class="skill-stat-item">
                            <span class="skill-stat-label">크리티컬</span>
                            <span class="skill-stat-value">${skill.crits}회 (${critRate}%)</span>
                        </div>
                        <div class="skill-stat-item">
                            <span class="skill-stat-label">추가타</span>
                            <span class="skill-stat-value">${skill.addHits}회 (${addHitRate}%)</span>
                        </div>
                    </div>
                </div>
            `;
        });

        this.elements.skillBarsContainer.innerHTML = html;    }    updateLogs() {
        if (!this.elements.logContainer) return;

        const filteredLogs = this.getFilteredLogs(); // 모든 필터된 로그 가져오기

        // 로그가 없는 경우
        if (filteredLogs.length === 0) {
            if (this.lastLogId !== null || this.lastLogCount > 0) {
                this.elements.logContainer.innerHTML = `
                    <div class="no-data-message">
                        <div style="font-size: 48px; margin-bottom: 16px;">📋</div>
                        <div style="font-size: 18px;">로그 데이터가 없습니다</div>
                    </div>
                `;
                this.lastLogId = null;
                this.lastLogCount = 0;
                this.virtualScroll.totalHeight = 0;
            }
            return;
        }

        // 마지막 로그 ID로 변경 감지
        const latestLog = filteredLogs[0];
        const currentLogId = `${latestLog.timestamp}_${latestLog.user_name}_${latestLog.damage}_${latestLog.skill_name}`;
        
        // 로그 수가 변경되었거나 새로운 로그가 있는 경우에만 업데이트
        const logsChanged = this.lastLogId !== currentLogId || filteredLogs.length !== this.lastLogCount;
        
        if (logsChanged) {
            this.setupVirtualScroll(filteredLogs);
            this.lastLogId = currentLogId;
            this.lastLogCount = filteredLogs.length;
        }
    }

    setupVirtualScroll(filteredLogs) {
        // 가상 스크롤 컨테이너 설정
        if (!this.elements.logContainer.querySelector('.virtual-scroll-container')) {
            this.initVirtualScrollContainer();
        }

        const container = this.elements.logContainer.querySelector('.virtual-scroll-container');
        const viewport = this.elements.logContainer.querySelector('.virtual-scroll-viewport');
        const content = this.elements.logContainer.querySelector('.virtual-scroll-content');

        // 전체 높이 계산
        this.virtualScroll.totalHeight = filteredLogs.length * this.virtualScroll.itemHeight;
        container.style.height = `${this.virtualScroll.totalHeight}px`;

        // 현재 스크롤 위치에 따른 보여줄 아이템 범위 계산
        this.calculateVisibleRange(filteredLogs.length);

        // 보여줄 로그들만 렌더링
        this.renderVisibleLogs(filteredLogs);
    }

    initVirtualScrollContainer() {
        this.elements.logContainer.innerHTML = `
            <div class="virtual-scroll-viewport">
                <div class="virtual-scroll-container">
                    <div class="virtual-scroll-content"></div>
                </div>
            </div>
        `;

        // 스크롤 이벤트 리스너 추가
        const viewport = this.elements.logContainer.querySelector('.virtual-scroll-viewport');
        viewport.addEventListener('scroll', () => {
            this.virtualScroll.scrollTop = viewport.scrollTop;
            this.onVirtualScroll();
        });
    }

    calculateVisibleRange(totalItems) {
        const viewport = this.elements.logContainer.querySelector('.virtual-scroll-viewport');
        const viewportHeight = viewport ? viewport.clientHeight : 400;
        
        this.virtualScroll.startIndex = Math.floor(this.virtualScroll.scrollTop / this.virtualScroll.itemHeight);
        this.virtualScroll.endIndex = Math.min(
            totalItems - 1,
            this.virtualScroll.startIndex + Math.ceil(viewportHeight / this.virtualScroll.itemHeight) + 5 // 여분 렌더링
        );
        
        // 시작 인덱스가 음수가 되지 않도록
        this.virtualScroll.startIndex = Math.max(0, this.virtualScroll.startIndex);
    }

    renderVisibleLogs(filteredLogs) {
        const content = this.elements.logContainer.querySelector('.virtual-scroll-content');
        if (!content) return;

        const visibleLogs = filteredLogs.slice(this.virtualScroll.startIndex, this.virtualScroll.endIndex + 1);
        
        let html = '';
        visibleLogs.forEach((log, index) => {
            const actualIndex = this.virtualScroll.startIndex + index;
            html += this.createLogElementHTML(log, actualIndex);
        });

        content.innerHTML = html;
        
        // 컨텐츠 위치 조정
        const offsetY = this.virtualScroll.startIndex * this.virtualScroll.itemHeight;
        content.style.transform = `translateY(${offsetY}px)`;
    }

    onVirtualScroll() {
        const filteredLogs = this.getFilteredLogs();
        if (filteredLogs.length === 0) return;

        this.calculateVisibleRange(filteredLogs.length);
        this.renderVisibleLogs(filteredLogs);
    }

    createLogElementHTML(log, index = null) {
        const timestamp = new Date(log.timestamp).toLocaleTimeString();
        const critClass = log.is_crit ? 'log-crit' : '';
        const displaySkillName = this.getSkillDisplayName(log.skill_name);
        
        // 특수 효과 플래그들을 배열로 수집
        const flags = [];
        if (log.is_crit) flags.push('크리티컬');
        if (log.is_add_hit) flags.push('추가타');
        if (log.is_unguarded) flags.push('무방비');
        if (log.is_break) flags.push('브레이크');
        if (log.is_first_hit) flags.push('선타');
        if (log.is_power) flags.push('파워');
        if (log.is_fast) flags.push('고속');
        
        // 속성 효과들
        const elements = [];
        if (log.is_ice) elements.push('빙결');
        if (log.is_fire) elements.push('화상');
        if (log.is_electric) elements.push('감전');
        if (log.is_holy) elements.push('신성');
        if (log.is_bleed) elements.push('출혈');
        if (log.is_poison) elements.push('중독');
        if (log.is_mind) elements.push('정신');
        if (log.is_dot) elements.push('지속피해');
        
        // 플래그 텍스트 생성
        let flagText = '';
        if (flags.length > 0 || elements.length > 0) {
            const allEffects = [...flags, ...elements];
            flagText = ` <span class="log-flags">[${allEffects.join(', ')}]</span>`;
        }        return `
            <div class="log-item" style="height: ${this.virtualScroll.itemHeight}px;">
                <div class="log-info">
                    <span class="log-timestamp">${timestamp}</span> 
                    <strong>${log.user_name}</strong>이(가) 
                    <span class="log-target">${log.target_name}</span>에게 
                    <span class="log-skill">${displaySkillName}</span>으로 
                    <span class="log-damage ${critClass}">${this.formatNumber(log.damage, false)}</span> 데미지
                    ${flagText}
                </div>
            </div>
        `;
    }

    updateSelectionLists() {
        this.updateTargetList();
        this.updateUserList();
    }

    updateTargetList() {
        if (!this.elements.targetList) return;

        const targets = Array.from(this.targetData.values())
            .sort((a, b) => b.totalDamage - a.totalDamage);

        if (targets.length === 0) {
            this.elements.targetList.innerHTML = '<div class="no-data-message">타겟 데이터를 기다리는 중...</div>';
            return;
        }

        let html = '';
        targets.forEach(target => {
            const selectedClass = this.selectedTarget === target.name ? 'selected' : '';
            html += `
                <div class="selection-item ${selectedClass}" onclick="app.selectTarget('${target.name}')">
                    ${target.name} (${this.formatNumber(target.totalDamage)})
                </div>
            `;
        });

        this.elements.targetList.innerHTML = html;
    }

    updateUserList() {
        if (!this.elements.userList) return;

        const users = Array.from(this.damageData.values())
            .sort((a, b) => b.totalDamage - a.totalDamage);

        if (users.length === 0) {
            this.elements.userList.innerHTML = '<div class="no-data-message">사용자 데이터를 기다리는 중...</div>';
            return;
        }

        let html = '';
        users.forEach(user => {
            const selectedClass = this.selectedUser === user.name ? 'selected' : '';
            html += `
                <div class="selection-item ${selectedClass}" onclick="app.selectUser('${user.name}')">
                    ${user.name} (${this.formatNumber(user.totalDamage)})
                </div>
            `;
        });

        this.elements.userList.innerHTML = html;
    }

    // ========== 필터링 ==========
    getFilteredUsers() {
        let filtered = new Map(this.damageData);

        // 선택된 사용자 필터
        if (this.selectedUser) {
            const selectedData = filtered.get(this.selectedUser);
            if (selectedData) {
                filtered = new Map([[this.selectedUser, selectedData]]);
            } else {
                filtered = new Map();
            }
        }

        return filtered;
    }

    getFilteredLogs() {
        let filtered = [...this.logData];

        // 타겟 필터
        if (this.selectedTarget) {
            filtered = filtered.filter(log => log.target_name === this.selectedTarget);
        }

        // 사용자 필터
        if (this.selectedUser) {
            filtered = filtered.filter(log => log.user_name === this.selectedUser);
        }

        // 스킬 필터
        if (this.filters.skillFilter) {
            const skillFilter = this.filters.skillFilter.toLowerCase();
            filtered = filtered.filter(log => {
                const displayName = this.getSkillDisplayName(log.skill_name);
                return log.skill_name.toLowerCase().includes(skillFilter) ||
                       displayName.toLowerCase().includes(skillFilter);
            });
        }

        // 도트 데미지 필터
        if (this.filters.filterDot) {
            filtered = filtered.filter(log => 
                !log.skill_name.toLowerCase().includes('dot') &&
                !log.skill_name.toLowerCase().includes('도트')
            );
        }

        return filtered;
    }

    updateFilter() {
        if (this.elements.skillFilter) {
            this.filters.skillFilter = this.elements.skillFilter.value;
        }
        if (this.elements.filterDot) {
            this.filters.filterDot = this.elements.filterDot.checked;
        }
        if (this.elements.autoReset) {
            this.filters.autoReset = this.elements.autoReset.checked;
        }
    }

    // ========== 선택 관리 ==========
    selectTarget(targetName) {
        this.selectedTarget = this.selectedTarget === targetName ? null : targetName;
        if (this.elements.selectedTargetDisplay) {
            this.elements.selectedTargetDisplay.textContent = 
                this.selectedTarget || '전체 타겟';
        }
    }

    selectUser(userName) {
        this.selectedUser = this.selectedUser === userName ? null : userName;
        if (this.elements.selectedUserDisplay) {
            this.elements.selectedUserDisplay.textContent = 
                this.selectedUser || '전체 사용자';
        }
    }

    clearSelectedTarget() {
        this.selectedTarget = null;
        if (this.elements.selectedTargetDisplay) {
            this.elements.selectedTargetDisplay.textContent = '전체 타겟';
        }
    }

    clearSelectedUser() {
        this.selectedUser = null;
        if (this.elements.selectedUserDisplay) {
            this.elements.selectedUserDisplay.textContent = '전체 사용자';
        }
    }

    // ========== 탭 관리 ==========
    switchTab(tabName) {
        // 모든 탭 버튼과 콘텐츠 비활성화
        this.elements.tabBtns.forEach(btn => btn.classList.remove('active'));
        this.elements.tabContents.forEach(content => content.classList.remove('active'));

        // 선택된 탭 활성화
        const selectedBtn = document.querySelector(`[data-tab="${tabName}"]`);
        const selectedContent = document.getElementById(`${tabName}-tab`);

        if (selectedBtn && selectedContent) {
            selectedBtn.classList.add('active');
            selectedContent.classList.add('active');
        }
    }

    // ========== 테마 관리 ==========
    toggleTheme() {
        const currentTheme = document.documentElement.getAttribute('data-theme');
        const newTheme = currentTheme === 'dark' ? 'light' : 'dark';
        
        document.documentElement.setAttribute('data-theme', newTheme);
        localStorage.setItem('theme', newTheme);
        
        // 테마 아이콘 업데이트
        if (this.elements.themeToggle) {
            const themeIcon = this.elements.themeToggle.querySelector('.theme-icon');
            if (themeIcon) {
                themeIcon.textContent = newTheme === 'dark' ? '☀️' : '🌙';
            }
        }
    }

    loadTheme() {
        const savedTheme = localStorage.getItem('theme') || 'light';
        document.documentElement.setAttribute('data-theme', savedTheme);
        
        if (this.elements.themeToggle) {
            const themeIcon = this.elements.themeToggle.querySelector('.theme-icon');
            if (themeIcon) {
                themeIcon.textContent = savedTheme === 'dark' ? '☀️' : '🌙';
            }        }
    }    // ========== 데이터 관리 ==========
    resetData() {
        this.damageData.clear();
        this.targetData.clear();
        this.logData = [];
        this.sessionStartTime = null;
        this.statistics = {
            totalHits: 0,
            totalCrits: 0,
            totalAddHits: 0,
            totalDamage: 0
        };
        
        // 로그 추적 변수 초기화
        this.lastLogCount = 0;
        this.lastLogId = null;
        
        // 가상 스크롤 상태 초기화
        this.virtualScroll.scrollTop = 0;
        this.virtualScroll.totalHeight = 0;
        this.virtualScroll.startIndex = 0;
        this.virtualScroll.endIndex = 0;
        
        this.updateSessionStatus('⚪ 초기화됨');
        console.log('데이터가 초기화되었습니다.');
    }    saveData() {
        // 현재 데이터를 JSON 파일로 내보내기 (백업 목적)
        const data = {
            damageData: Array.from(this.damageData.entries()).map(([key, value]) => [
                key,
                {
                    ...value,
                    skills: Array.from(value.skills.entries())
                }
            ]),
            targetData: Array.from(this.targetData.entries()),
            logData: this.logData,
            sessionStartTime: this.sessionStartTime,
            statistics: this.statistics,
            timestamp: Date.now()
        };

        const blob = new Blob([JSON.stringify(data, null, 2)], { type: 'application/json' });
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = `damage-meter-${new Date().toISOString().slice(0, 19).replace(/:/g, '-')}.json`;
        a.click();
        URL.revokeObjectURL(url);
        
        console.log('데이터가 JSON 파일로 내보내졌습니다.');
    }

    loadData() {
        if (this.elements.fileInput) {
            this.elements.fileInput.click();
        }
    }    handleFileLoad(event) {
        const file = event.target.files[0];
        if (!file) return;

        const reader = new FileReader();
        reader.onload = (e) => {
            try {
                const data = JSON.parse(e.target.result);
                
                // JSON 파일에서 데이터 복원
                this.damageData = new Map(data.damageData.map(([key, value]) => [
                    key,
                    {
                        ...value,
                        skills: new Map(value.skills)
                    }
                ]));
                
                this.targetData = new Map(data.targetData);
                this.logData = data.logData || [];
                this.sessionStartTime = data.sessionStartTime;
                this.statistics = data.statistics || {
                    totalHits: 0,
                    totalCrits: 0,
                    totalAddHits: 0,
                    totalDamage: 0
                };
                
                this.currentProfile = null; // 외부 파일에서 불러온 경우 프로필 해제
                
                console.log('JSON 파일에서 데이터가 불러와졌습니다.');
                alert('JSON 파일에서 데이터가 불러와졌습니다.');
                
            } catch (error) {
                console.error('파일 로드 오류:', error);
                alert('파일을 불러오는 중 오류가 발생했습니다.');
            }
        };
        
        reader.readAsText(file);
        event.target.value = ''; // 파일 입력 초기화
    }exportCSV() {
        const filteredLogs = this.getFilteredLogs();

        if (filteredLogs.length === 0) {
            alert('내보낼 데미지 로그가 없습니다.');
            return;
        }

        // CSV 헤더
        let csv = 'Timestamp,User,Target,Skill,Damage,Is_Crit,Is_Add_Hit,Is_Unguarded,Is_Break,Is_First_Hit,Is_Default_Attack,Is_Multi_Attack,Is_Power,Is_Fast,Is_DoT,Is_Ice,Is_Fire,Is_Electric,Is_Holy,Is_Dark,Is_Bleed,Is_Poison,Is_Mind,Skill_ID\n';
        
        // 각 로그를 CSV 행으로 변환
        filteredLogs.forEach(log => {
            const timestamp = new Date(log.timestamp).toLocaleString('ko-KR');
            const displaySkillName = this.getSkillDisplayName(log.skill_name);
            
            // 플래그들을 0/1로 변환
            const row = [
                `"${timestamp}"`,
                `"${log.user_name}"`,
                `"${log.target_name}"`,
                `"${displaySkillName}"`,
                log.damage,
                log.is_crit ? 1 : 0,
                log.is_add_hit ? 1 : 0,
                log.is_unguarded ? 1 : 0,
                log.is_break ? 1 : 0,
                log.is_first_hit ? 1 : 0,
                log.is_default_attack ? 1 : 0,
                log.is_multi_attack ? 1 : 0,
                log.is_power ? 1 : 0,
                log.is_fast ? 1 : 0,
                log.is_dot ? 1 : 0,
                log.is_ice ? 1 : 0,
                log.is_fire ? 1 : 0,
                log.is_electric ? 1 : 0,
                log.is_holy ? 1 : 0,
                log.is_dark ? 1 : 0,
                log.is_bleed ? 1 : 0,
                log.is_poison ? 1 : 0,
                log.is_mind ? 1 : 0,
                log.skill_id || 0
            ];
            
            csv += row.join(',') + '\n';
        });

        const blob = new Blob([csv], { type: 'text/csv;charset=utf-8' });
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = `damage-logs-${new Date().toISOString().slice(0, 19).replace(/:/g, '-')}.csv`;
        a.click();
        URL.revokeObjectURL(url);
        
        console.log(`CSV 파일이 내보내졌습니다. (${filteredLogs.length}개 로그)`);
    }

    // ========== 키보드 단축키 ==========
    handleKeyboard(event) {
        if (event.ctrlKey) {
            switch (event.code) {
                case 'KeyR':
                    event.preventDefault();
                    this.resetData();
                    break;
                case 'KeyS':
                    event.preventDefault();
                    this.saveData();
                    break;
                case 'KeyO':
                    event.preventDefault();
                    this.loadData();
                    break;
                case 'KeyE':
                    event.preventDefault();
                    this.exportCSV();
                    break;
                case 'KeyT':
                    event.preventDefault();
                    this.toggleTheme();
                    break;
            }
        }

        // 탭 단축키 (1, 2, 3)
        if (event.code >= 'Digit1' && event.code <= 'Digit3') {
            const tabs = ['ranking', 'statistics', 'logs'];
            const tabIndex = parseInt(event.code.slice(-1)) - 1;
            if (tabs[tabIndex]) {
                this.switchTab(tabs[tabIndex]);
            }
        }
    }

    // ========== 업데이트 루프 ==========
    startUpdateLoop() {
        setInterval(() => {
            this.calculateDPS();
            this.updateUI();        }, 1000); // 1초마다 업데이트
    }

    // ========== 유틸리티 함수 ==========
    formatNumber(num, useShortFormat = true) {
        if (!useShortFormat) {
            // 실제 숫자를 콤마로 구분하여 표시
            return num.toLocaleString('ko-KR');
        }
        
        // 기존 K, M 단위 표시 (랭킹, 요약 등에서 사용)
        if (num >= 1000000) {
            return (num / 1000000).toFixed(1) + 'M';
        } else if (num >= 1000) {
            return (num / 1000).toFixed(1) + 'K';
        }
        return num.toString();
    }

    // ========== 테스트 데이터 생성 ==========
    generateTestData() {
        console.log('테스트 데이터 생성 중...');
        
        const testUsers = ['플레이어1', '플레이어2', '플레이어3', '플레이어4', '플레이어5'];
        const testTargets = ['슬라임킹', '오크족장', '드래곤', '보스몬스터'];
        const testSkills = [
            'MeleeDefaultAttack_1',
            'SwordMaster_SteelWedge',
            'Fighter_ChargingFist_End_LV3',
            'Bard_TripleStroke',
            'Monk_Skill_SurgeOfLight_01'
        ];

        // 세션 시작
        this.sessionStartTime = Date.now();
        this.updateSessionStatus('🟢 테스트 진행중');

        // 100개의 테스트 데미지 로그 생성
        for (let i = 0; i < 100; i++) {
            const testData = {
                timestamp: Date.now() - (100 - i) * 1000, // 100초에 걸쳐 분산
                user_name: testUsers[Math.floor(Math.random() * testUsers.length)],
                target_name: testTargets[Math.floor(Math.random() * testTargets.length)],
                skill_name: testSkills[Math.floor(Math.random() * testSkills.length)],
                damage: Math.floor(Math.random() * 5000) + 1000, // 1000-6000 데미지
                is_crit: Math.random() < 0.2, // 20% 크리티컬
                is_add_hit: Math.random() < 0.15 // 15% 추가타
            };

            this.processDamageData(testData);
        }

        console.log('테스트 데이터 생성 완료!');
    }

    // ========== 프로필 관리 ==========
    loadProfiles() {
        try {
            const savedProfiles = localStorage.getItem('damageProfiles');
            if (savedProfiles) {
                const profilesData = JSON.parse(savedProfiles);
                this.profiles = new Map();
                
                Object.entries(profilesData).forEach(([name, data]) => {
                    this.profiles.set(name, {
                        ...data,
                        damageData: new Map(data.damageData?.map(([key, value]) => [
                            key,
                            {
                                ...value,
                                skills: new Map(value.skills || [])
                            }
                        ]) || []),
                        targetData: new Map(data.targetData || []),
                        logData: data.logData || []
                    });
                });
                
                console.log(`${this.profiles.size}개의 프로필을 불러왔습니다.`);
            }
        } catch (error) {
            console.error('프로필 불러오기 오류:', error);
        }
        this.updateProfileList();
    }

    saveProfile() {
        const profileName = this.elements.profileName?.value?.trim();
        if (!profileName) {
            alert('프로필 이름을 입력해주세요.');
            return;
        }

        if (this.damageData.size === 0 && this.logData.length === 0) {
            alert('저장할 데이터가 없습니다.');
            return;
        }

        const profileData = {
            name: profileName,
            createdAt: new Date().toISOString(),
            damageData: Array.from(this.damageData.entries()).map(([key, value]) => [
                key,
                {
                    ...value,
                    skills: Array.from(value.skills.entries())
                }
            ]),
            targetData: Array.from(this.targetData.entries()),
            logData: this.logData,
            sessionStartTime: this.sessionStartTime,
            statistics: this.statistics
        };

        this.profiles.set(profileName, profileData);
        this.saveProfilesToStorage();
        
        if (this.elements.profileName) {
            this.elements.profileName.value = '';
        }
        
        console.log(`프로필 "${profileName}"이 저장되었습니다.`);
        alert(`프로필 "${profileName}"이 저장되었습니다.`);
    }

    saveProfilesToStorage() {
        try {
            const profilesData = {};
            this.profiles.forEach((data, name) => {
                profilesData[name] = {
                    ...data,
                    damageData: data.damageData instanceof Map ? 
                        Array.from(data.damageData.entries()) : data.damageData,
                    targetData: data.targetData instanceof Map ? 
                        Array.from(data.targetData.entries()) : data.targetData
                };
            });
            
            localStorage.setItem('damageProfiles', JSON.stringify(profilesData));
        } catch (error) {
            console.error('프로필 저장 오류:', error);
            alert('프로필 저장 중 오류가 발생했습니다.');
        }
    }

    loadProfile(profileName) {
        const profile = this.profiles.get(profileName);
        if (!profile) {
            alert('프로필을 찾을 수 없습니다.');
            return;
        }

        // 현재 데이터를 프로필 데이터로 교체
        this.damageData = new Map(profile.damageData);
        this.targetData = new Map(profile.targetData);
        this.logData = [...(profile.logData || [])];
        this.sessionStartTime = profile.sessionStartTime;
        this.statistics = { ...profile.statistics };
        this.currentProfile = profileName;

        // UI 업데이트
        this.updateUI();
        
        console.log(`프로필 "${profileName}"을 불러왔습니다.`);
        alert(`프로필 "${profileName}"을 불러왔습니다.`);
    }

    deleteProfile(profileName) {
        if (!confirm(`프로필 "${profileName}"을 삭제하시겠습니까?`)) {
            return;
        }

        this.profiles.delete(profileName);
        this.saveProfilesToStorage();
        
        // 현재 비교 중인 프로필이 삭제된 경우
        if (this.compareProfile === profileName) {
            this.compareProfile = null;
            this.isComparisonMode = false;
            if (this.elements.compareToggle) {
                this.elements.compareToggle.checked = false;
            }
        }
        
        console.log(`프로필 "${profileName}"이 삭제되었습니다.`);
        alert(`프로필 "${profileName}"이 삭제되었습니다.`);
    }

    updateProfileList() {
        // 프로필 목록 업데이트
        if (this.elements.profileList) {
            let html = '';
            if (this.profiles.size === 0) {
                html = '<div class="no-data-message">저장된 프로필이 없습니다.</div>';
            } else {
                this.profiles.forEach((profile, name) => {
                    const isCurrentProfile = this.currentProfile === name;
                    const createdDate = new Date(profile.createdAt).toLocaleDateString('ko-KR');
                    
                    html += `
                        <div class="profile-item ${isCurrentProfile ? 'current' : ''}">
                            <div class="profile-info">
                                <div class="profile-name">${name}</div>
                                <div class="profile-date">${createdDate}</div>
                                <div class="profile-stats">
                                    사용자: ${profile.damageData?.length || 0}명, 
                                    로그: ${profile.logData?.length || 0}개
                                </div>
                            </div>
                            <div class="profile-actions">
                                <button onclick="app.loadProfile('${name}')" class="btn-small">불러오기</button>
                                <button onclick="app.deleteProfile('${name}')" class="btn-small btn-danger">삭제</button>
                            </div>
                        </div>
                    `;
                });
            }
            this.elements.profileList.innerHTML = html;
        }

        // 비교 프로필 선택 업데이트
        if (this.elements.compareProfileSelect) {
            let options = '<option value="">비교할 프로필 선택</option>';
            this.profiles.forEach((profile, name) => {
                if (name !== this.currentProfile) {
                    const selected = this.compareProfile === name ? 'selected' : '';
                    options += `<option value="${name}" ${selected}>${name}</option>`;
                }
            });
            this.elements.compareProfileSelect.innerHTML = options;
        }
    }    toggleComparison() {
        this.isComparisonMode = this.elements.compareToggle?.checked || false;
        
        if (this.isComparisonMode) {
            const selectedProfile = this.elements.compareProfileSelect?.value;
            if (!selectedProfile) {
                alert('비교할 프로필을 선택해주세요.');
                if (this.elements.compareToggle) {
                    this.elements.compareToggle.checked = false;
                }
                this.isComparisonMode = false;
                return;
            }
            this.compareProfile = selectedProfile;
            console.log(`비교 모드 활성화: ${this.currentProfile} vs ${this.compareProfile}`);
            this.updateComparisonView();
        } else {
            this.compareProfile = null;
            console.log('비교 모드 비활성화');
            this.hideComparisonView();
        }
        
        this.updateUI();
    }

    updateComparisonView() {
        const comparisonResult = document.getElementById('comparison-result');
        const currentProfileNameEl = document.getElementById('current-profile-name');
        const compareProfileNameEl = document.getElementById('compare-profile-name');
        const comparisonDetails = document.getElementById('comparison-details');
        
        if (!comparisonResult || !this.isComparisonMode || !this.compareProfile) {
            return;
        }
        
        // 비교 결과 표시
        comparisonResult.style.display = 'block';
        
        if (currentProfileNameEl) {
            currentProfileNameEl.textContent = this.currentProfile || '현재 세션';
        }
        if (compareProfileNameEl) {
            compareProfileNameEl.textContent = this.compareProfile;
        }
        
        // 비교 데이터 생성
        const compareData = this.getComparisonData();
        if (compareData && comparisonDetails) {
            this.renderComparisonDetails(comparisonDetails, compareData);
        }
    }

    hideComparisonView() {
        const comparisonResult = document.getElementById('comparison-result');
        if (comparisonResult) {
            comparisonResult.style.display = 'none';
        }
    }

    renderComparisonDetails(container, compareData) {
        const currentStats = this.statistics;
        const compareStats = compareData.statistics;
        
        const statsToCompare = [
            { key: 'totalDamage', label: '총 데미지' },
            { key: 'totalHits', label: '총 타격 수' },
            { key: 'totalCrits', label: '크리티컬 횟수' },
            { key: 'totalAddHits', label: '추가타 횟수' }
        ];
        
        let html = '';
        statsToCompare.forEach(stat => {
            const currentValue = currentStats[stat.key] || 0;
            const compareValue = compareStats[stat.key] || 0;
            const diff = currentValue - compareValue;
            const diffPercent = compareValue > 0 ? ((diff / compareValue) * 100).toFixed(1) : '0.0';
            
            let diffClass = 'neutral';
            let diffText = '동일';
            
            if (diff > 0) {
                diffClass = 'positive';
                diffText = `+${this.formatNumber(diff)} (+${diffPercent}%)`;
            } else if (diff < 0) {
                diffClass = 'negative';
                diffText = `${this.formatNumber(diff)} (${diffPercent}%)`;
            }
            
            html += `
                <div class="comparison-stat">
                    <div class="comparison-stat-current">
                        <div class="comparison-stat-value">${this.formatNumber(currentValue)}</div>
                        <div class="comparison-stat-diff ${diffClass}">${diffText}</div>
                    </div>
                    <div class="comparison-stat-label">${stat.label}</div>
                    <div class="comparison-stat-compare">
                        <div class="comparison-stat-value">${this.formatNumber(compareValue)}</div>
                        <div class="comparison-stat-diff neutral">기준값</div>
                    </div>
                </div>
            `;
        });
        
        container.innerHTML = html;
    }

    getComparisonData() {
        if (!this.isComparisonMode || !this.compareProfile) {
            return null;
        }
        
        const compareProfileData = this.profiles.get(this.compareProfile);
        if (!compareProfileData) {
            return null;
        }
        
        return {
            name: this.compareProfile,
            damageData: compareProfileData.damageData,
            targetData: compareProfileData.targetData,
            logData: compareProfileData.logData,
            statistics: compareProfileData.statistics
        };
    }
}

// ========== 애플리케이션 시작 ==========
let app;

document.addEventListener('DOMContentLoaded', () => {
    app = new DamageMeterApp();
    console.log('데미지 미터 애플리케이션이 시작되었습니다.');
});

// ========== 전역 함수 (HTML에서 호출) ==========
window.app = {
    selectTarget: (targetName) => app?.selectTarget(targetName),
    selectUser: (userName) => app?.selectUser(userName),
    clearSelectedTarget: () => app?.clearSelectedTarget(),
    clearSelectedUser: () => app?.clearSelectedUser(),
    resetData: () => app?.resetData(),
    saveData: () => app?.saveData(),
    loadData: () => app?.loadData(),
    exportCSV: () => app?.exportCSV(),
    updateFilter: () => app?.updateFilter(),
    generateTestData: () => app?.generateTestData(),
    // 프로필 관리 함수들
    saveProfile: () => app?.saveProfile(),
    loadProfile: (profileName) => app?.loadProfile(profileName),
    deleteProfile: (profileName) => app?.deleteProfile(profileName),
    toggleComparison: () => app?.toggleComparison(),
    updateComparisonView: () => app?.updateComparisonView()
};
