using Wyrd;
using Wyrd.Processes;

namespace Telecom;

public static partial class Flows
{
    public static string PortKey(string id, string msisdn, long epoch) => $"{id}/{msisdn}/{epoch}";

    public static string ProfileKey(string id, string profile, int attempt) => $"{id}/{profile}/{attempt}";
}
