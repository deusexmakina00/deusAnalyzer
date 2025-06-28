-- packet_config.lua
-- Lua configuration for packet filtering and processing

-- List of packet types to exclude from processing
excludes = {
    10318,
    100043, -- 네트워크?
    100044, -- 타겟과 관련
    100047, -- 동기화?
    100049, -- 동기화?
    100081, -- 타겟과 관련
    100085, -- 시스템
    100090,
    100093, -- 시스템
    100177, -- 타겟과 관련
    100180, -- 타겟과 관련
    100252,
    100253, -- 행동 상태?
    100278, -- 캐릭터 행동 루프
    100317, -- 타겟과 관련
    100582, -- 위치와 관련
    100585, -- 위치와 관련
    100587, -- 위치와 관련
    100589, -- 이동 정보
    100590, -- 이동 방향
    100594, -- 위치와 관련
    100600, -- 상태 모니터링 패킷일 가능성 높음.
    100828, -- 엔티티 상태
    100835, -- 효과?
}

-- Dynamic filtering function
-- This function is called for each packet to determine if it should be filtered out
-- Parameters:
--   dataType (number): The packet data type
--   dataLength (number): The packet payload length
--   encodeType (number): The packet encoding type
-- Returns:
--   true if the packet should be excluded, false if it should be included
function shouldExcludePacket(dataType, dataLength, encodeType)
    -- Check static excludes list
    for _, excludeType in ipairs(excludes) do
        if dataType == excludeType then
            return true
        end
    end
    
    -- Dynamic filtering rules
    -- Example: Exclude very small packets that might be heartbeat/keepalive
    if dataLength < 4 then
        return true
    end
    
    -- Example: Exclude very large packets that might be file transfers
    if dataLength > 32768 then
        return true
    end
    
    -- Example: Exclude packets with invalid encoding types
    if encodeType < 0 or encodeType > 1 then
        return true
    end
    
    -- Add more dynamic rules here as needed
    -- For example, time-based filtering, rate limiting, etc.
    
    return false
end

-- Configuration for packet processing
config = {
    -- Maximum allowed packet length
    maxPacketLength = 65536,
    
    -- Minimum allowed packet length
    minPacketLength = 1,
    
    -- Valid data type range
    minDataType = 1,
    maxDataType = 200000,
    
    -- Valid encoding types
    validEncodingTypes = {0, 1}, -- 0 = uncompressed, 1 = brotli
    
    -- Enable/disable various features
    enableHexDump = true,
    enableMetadataGeneration = true,
    enablePacketSaving = true,
    
    -- Logging configuration
    logLevel = "INFO", -- DEBUG, INFO, WARN, ERROR
    logPacketTypes = true,
    logFilteredPackets = false,
}

-- Utility function to check if a value is in an array
function contains(table, value)
    for _, v in ipairs(table) do
        if v == value then
            return true
        end
    end
    return false
end

-- Advanced filtering function with context
-- This function can maintain state between calls and implement more complex logic
local packetCounts = {}
local lastPacketTime = {}

function shouldExcludePacketAdvanced(dataType, dataLength, encodeType, timestamp)
    -- Basic exclusion check
    if shouldExcludePacket(dataType, dataLength, encodeType) then
        return true
    end
    
    -- Rate limiting: exclude if too many packets of same type in short time
    local currentTime = timestamp or os.time()
    local count = packetCounts[dataType] or 0
    local lastTime = lastPacketTime[dataType] or 0
    
    -- Reset count if more than 1 second has passed
    if currentTime - lastTime > 1 then
        count = 0
    end
    
    count = count + 1
    packetCounts[dataType] = count
    lastPacketTime[dataType] = currentTime
    
    -- Exclude if more than 100 packets of same type per second
    if count > 100 then
        return true
    end
    
    return false
end

-- Function to get current configuration
function getConfig()
    return config
end

-- Function to update configuration at runtime
function updateConfig(key, value)
    if config[key] ~= nil then
        config[key] = value
        return true
    end
    return false
end

-- Function to add packet type to excludes list
function addExclude(dataType)
    table.insert(excludes, dataType)
end

-- Function to remove packet type from excludes list
function removeExclude(dataType)
    for i, v in ipairs(excludes) do
        if v == dataType then
            table.remove(excludes, i)
            break
        end
    end
end

-- Function to get current excludes list
function getExcludes()
    return excludes
end

print("Packet configuration loaded successfully")
