using System.Reflection;

namespace PacketCapture;

public static class PacketTypeRegistry
{
    /// <summary>
    /// Packet을 상속한 모든 타입의 TYPE 필드를 조사하여
    /// DataType : TypeName 형태의 사전을 반환합니다.
    /// </summary>
    /// <returns>Dictionary{int, string} (DataType : TypeName)</returns>
    public static Dictionary<int, string> GetPacketTypeMap()
    {
        var result = new Dictionary<int, string>();
        var packetBaseType = typeof(Packet);

        // 현재 어셈블리의 모든 타입 중 Packet을 상속한 클래스만 추출
        var types = Assembly
            .GetExecutingAssembly()
            .GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && packetBaseType.IsAssignableFrom(t));

        foreach (var type in types)
        {
            // TYPE 필드가 있는지 확인
            var typeField = type.GetField("TYPE", BindingFlags.Public | BindingFlags.Static);
            if (typeField != null && typeField.FieldType == typeof(int[]))
            {
                var typeArray = typeField.GetValue(null) as int[];
                if (typeArray != null)
                {
                    foreach (var dataType in typeArray)
                    {
                        // 중복 방지
                        if (!result.ContainsKey(dataType))
                            result[dataType] = type.Name;
                    }
                }
            }
        }
        return result;
    }
}
