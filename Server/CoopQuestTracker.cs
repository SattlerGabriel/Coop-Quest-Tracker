using PresenceService = FikaServer.Services.PresenceService;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers.Quest;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Services.Locales;
using SPTarkov.Server.Core.Utils;
using Range = SemanticVersioning.Range;
using Version = SemanticVersioning.Version;

namespace CoopQuestTracker;

public record ModMetadata : IModMetadata
{
    public string ModGuid { get; init; } = "com.boombah.coopquesttracker";
    public string Name { get; init; } = "CoopQuestTracker";
    public string Author { get; init; } = "Boombah";
    public List<string>? Contributors { get; init; }
    public Version Version { get; init; } = new("1.0.0");
    public Range SptVersion { get; init; } = new("~4.1.0");
    public List<string>? Incompatibilities { get; init; }
    public Dictionary<string, Range>? ModDependencies { get; init; } = new() { ["Fika"] = new("~2.4.0") };
    public string? Url { get; init; }
    public string License { get; init; } = "MIT";
    public bool HasPrepatcher { get; init; } = false;
}

public record QuestEntry(string Trader, string Name, string Status);
public record PlayerQuests(string Nickname, int Level, List<QuestEntry> Quests);

[Injectable]
public class CoopQuestRouter(CoopQuestService service, HttpResponseUtil httpResponseUtil, JsonUtil jsonUtil) : StaticRouter(jsonUtil, [
    new RouteAction<EmptyRequestData>(
        "/coopquesttracker/players",
        (url, info, sessionId, output, cancellationToken) =>
            new ValueTask<string>(httpResponseUtil.NoBody(service.GetOnlinePlayersQuests(sessionId))))
]);

[Injectable]
public class CoopQuestService(SaveServer saveServer, PresenceService presenceService, QuestHelper questHelper, LocaleService localeService)
{
    private static readonly HashSet<QuestStatusEnum> Active = [QuestStatusEnum.Started, QuestStatusEnum.AvailableForFinish];

    public List<PlayerQuests> GetOnlinePlayersQuests(MongoId sessionId)
    {
        var myPmc = saveServer.GetProfile(sessionId)?.CharacterData?.PmcData;
        var locale = localeService.GetLocaleDb();
        var myActive = myPmc?.Quests?
            .Where(q => Active.Contains(q.Status))
            .Select(q => (Id: q.QId, Trader: locale.GetValueOrDefault($"{questHelper.GetQuestFromDb(q.QId, myPmc)?.TraderId} Nickname", "?")))
            .ToList() ?? [];

        var result = new List<PlayerQuests>();
        foreach (var (id, profile) in saveServer.GetProfiles())
        {
            var pmc = profile.CharacterData?.PmcData;
            if (id == sessionId || pmc?.Info is null || presenceService.GetPlayerPresence(id) is null)
            {
                continue;
            }

            var theirs = questHelper.GetClientQuests(id).ToDictionary(q => q.Id, q => q.SptStatus);
            var quests = myActive
                .Select(q => new QuestEntry(q.Trader, questHelper.GetQuestNameFromLocale(q.Id), Classify(theirs.GetValueOrDefault(q.Id))))
                .OrderBy(q => q.Trader).ThenBy(q => q.Name)
                .ToList();
            result.Add(new PlayerQuests(pmc.Info.Nickname ?? "?", pmc.Info.Level ?? 0, quests));
        }

        return result;
    }

    public static string Classify(QuestStatusEnum? status) => status switch
    {
        QuestStatusEnum.Started or QuestStatusEnum.AvailableForFinish => "shared",
        QuestStatusEnum.Success => "completed",
        QuestStatusEnum.AvailableForStart or QuestStatusEnum.FailRestartable => "missing",
        _ => "locked"
    };
}
