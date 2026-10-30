using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using EnshroudedManager.Shared;

namespace EnshroudedManager.Service;

/// <summary>
/// Reads and writes enshrouded_server.json. Only the fields listed in the schema are touched:
/// every other key of the file (bannedAccounts, future settings…) is kept as is.
/// </summary>
public class ConfigStore
{
    private const double NsPerMinute = 60_000_000_000d;
    private const string Gs = "gameSettings.";

    private static readonly string[] Tags =
    [
        "LookingForPlayers", "English", "German", "French", "Italian", "Japanese", "Korean", "Polish", "Portuguese",
        "Russian", "Spanish", "Thai", "Turkish", "Ukrainian", "Chinese", "Taiwanese", "BaseBuilding", "Exploration", "Roleplay"
    ];

    // Ranges are the official ones from enshrouded_server_readme.txt. Going outside them is
    // allowed (the game accepts it) but reported as a warning.
    public static FieldDto[] Fields =>
    [
        new("name", L.T("Nom du serveur", "Server name"), L.T("Serveur", "Server"), "string", Help: L.T("Nom affiché dans la liste des serveurs.", "Name shown in the server browser.")),
        new("slotCount", L.T("Joueurs max", "Max players"), L.T("Serveur", "Server"), "int", 1, 16, 1),
        new("tags", L.T("Tags de recherche", "Search tags"), L.T("Serveur", "Server"), "tags", Options: Tags),
        new("enableTextChat", L.T("Chat texte", "Text chat"), L.T("Serveur", "Server"), "bool"),
        new("enableVoiceChat", L.T("Chat vocal", "Voice chat"), L.T("Serveur", "Server"), "bool"),
        new("voiceChatMode", L.T("Mode du chat vocal", "Voice chat mode"), L.T("Serveur", "Server"), "enum", Options: ["Proximity", "Global"]),
        new("ip", L.T("Adresse IP d'écoute", "Listen IP address"), L.T("Réseau", "Network"), "string", Help: L.T("Laisser 0.0.0.0 sauf besoin précis.", "Keep 0.0.0.0 unless you have a specific need.")),
        new("queryPort", L.T("Port de requête", "Query port"), L.T("Réseau", "Network"), "int", 1, 65535, 1, Help: L.T("Penser au pare-feu et à la box si modifié.", "Remember the firewall and router port forwarding if you change it.")),
        new("saveDirectory", L.T("Dossier de sauvegarde", "Save folder"), L.T("Réseau", "Network"), "string"),
        new("logDirectory", L.T("Dossier des logs", "Log folder"), L.T("Réseau", "Network"), "string"),
        new("gameSettingsPreset", L.T("Préréglage de difficulté", "Difficulty preset"), L.T("Difficulté", "Difficulty"), "enum",
            Options: ["Default", "Relaxed", "Hard", "Survival", "Custom"],
            Help: L.T("Les réglages détaillés ci-dessous ne s'appliquent qu'avec « Custom ».", "The detailed settings below only apply with “Custom”.")),

        new(Gs + "playerHealthFactor", L.T("Santé", "Health"), L.T("Joueurs", "Players"), "float", 0.25, 4, 0.05, Unit: "×"),
        new(Gs + "playerManaFactor", L.T("Mana", "Mana"), L.T("Joueurs", "Players"), "float", 0.25, 4, 0.05, Unit: "×"),
        new(Gs + "playerStaminaFactor", L.T("Endurance", "Stamina"), L.T("Joueurs", "Players"), "float", 0.25, 4, 0.05, Unit: "×"),
        new(Gs + "playerBodyHeatFactor", L.T("Résistance au froid", "Cold resistance"), L.T("Joueurs", "Players"), "float", 0.5, 2, 0.05, Unit: "×"),
        new(Gs + "playerDivingTimeFactor", L.T("Temps de plongée", "Diving time"), L.T("Joueurs", "Players"), "float", 0.5, 2, 0.05, Unit: "×"),
        new(Gs + "enableDurability", L.T("Usure des armes", "Weapon durability"), L.T("Joueurs", "Players"), "bool"),
        new(Gs + "enableStarvingDebuff", L.T("Faim et famine", "Hunger and starvation"), L.T("Joueurs", "Players"), "bool"),
        new(Gs + "foodBuffDurationFactor", L.T("Durée des bonus de nourriture", "Food buff duration"), L.T("Joueurs", "Players"), "float", 0.5, 2, 0.05, Unit: "×"),
        new(Gs + "fromHungerToStarving", L.T("Délai faim → famine", "Hunger to starvation delay"), L.T("Joueurs", "Players"), "duration", 5, 20, 1, Unit: "min"),
        new(Gs + "shroudTimeFactor", L.T("Temps dans le Voile", "Time in the Shroud"), L.T("Joueurs", "Players"), "float", 0.5, 2, 0.05, Unit: "×"),
        new(Gs + "tombstoneMode", L.T("À la mort", "On death"), L.T("Joueurs", "Players"), "enum", Options: ["AddBackpackMaterials", "Everything", "NoTombstone"],
            Help: L.T("AddBackpackMaterials : perte des matériaux · Everything : perte de tout · NoTombstone : rien n'est perdu.", "AddBackpackMaterials: lose materials · Everything: lose everything · NoTombstone: lose nothing.")),
        new(Gs + "enableGliderTurbulences", L.T("Turbulences en planeur", "Glider turbulences"), L.T("Joueurs", "Players"), "bool"),

        new(Gs + "miningDamageFactor", L.T("Efficacité du minage", "Mining efficiency"), L.T("Ressources & XP", "Resources & XP"), "float", 0.5, 2, 0.05, Unit: "×"),
        new(Gs + "plantGrowthSpeedFactor", L.T("Vitesse de pousse des plantes", "Plant growth speed"), L.T("Ressources & XP", "Resources & XP"), "float", 0.25, 2, 0.05, Unit: "×"),
        new(Gs + "resourceDropStackAmountFactor", L.T("Quantité de ressources", "Resource yield"), L.T("Ressources & XP", "Resources & XP"), "float", 0.25, 2, 0.05, Unit: "×"),
        new(Gs + "factoryProductionSpeedFactor", L.T("Vitesse des ateliers", "Workstation speed"), L.T("Ressources & XP", "Resources & XP"), "float", 0.25, 2, 0.05, Unit: "×"),
        new(Gs + "perkUpgradeRecyclingFactor", L.T("Runes récupérées au recyclage", "Runes back when salvaging"), L.T("Ressources & XP", "Resources & XP"), "float", 0, 1, 0.05, Unit: "×"),
        new(Gs + "perkCostFactor", L.T("Coût d'amélioration des armes", "Weapon upgrade cost"), L.T("Ressources & XP", "Resources & XP"), "float", 0.25, 2, 0.05, Unit: "×"),
        new(Gs + "experienceCombatFactor", L.T("XP de combat", "Combat XP"), L.T("Ressources & XP", "Resources & XP"), "float", 0.25, 2, 0.05, Unit: "×"),
        new(Gs + "experienceMiningFactor", L.T("XP de minage", "Mining XP"), L.T("Ressources & XP", "Resources & XP"), "float", 0, 2, 0.05, Unit: "×"),
        new(Gs + "experienceExplorationQuestsFactor", L.T("XP d'exploration et quêtes", "Exploration & quest XP"), L.T("Ressources & XP", "Resources & XP"), "float", 0.25, 2, 0.05, Unit: "×"),
        new(Gs + "fishingDifficulty", L.T("Difficulté de la pêche", "Fishing difficulty"), L.T("Ressources & XP", "Resources & XP"), "enum", Options: ["VeryEasy", "Easy", "Normal", "Hard", "VeryHard"]),

        new(Gs + "randomSpawnerAmount", L.T("Quantité d'ennemis", "Enemy amount"), L.T("Ennemis", "Enemies"), "enum", Options: ["Few", "Normal", "Automatic", "Many", "Extreme"]),
        new(Gs + "aggroPoolAmount", L.T("Ennemis attaquant en même temps", "Enemies attacking at once"), L.T("Ennemis", "Enemies"), "enum", Options: ["Few", "Normal", "Many", "Extreme"]),
        new(Gs + "enemyDamageFactor", L.T("Dégâts des ennemis", "Enemy damage"), L.T("Ennemis", "Enemies"), "float", 0.25, 5, 0.05, Unit: "×"),
        new(Gs + "enemyHealthFactor", L.T("Santé des ennemis", "Enemy health"), L.T("Ennemis", "Enemies"), "float", 0.25, 4, 0.05, Unit: "×"),
        new(Gs + "enemyStaminaFactor", L.T("Résistance à l'étourdissement", "Stun resistance"), L.T("Ennemis", "Enemies"), "float", 0.5, 2, 0.05, Unit: "×"),
        new(Gs + "enemyPerceptionRangeFactor", L.T("Perception des ennemis", "Enemy perception"), L.T("Ennemis", "Enemies"), "float", 0.5, 2, 0.05, Unit: "×"),
        new(Gs + "threatBonus", L.T("Fréquence des attaques", "Attack frequency"), L.T("Ennemis", "Enemies"), "float", 0.25, 4, 0.05, Unit: "×"),
        new(Gs + "bossDamageFactor", L.T("Dégâts des boss", "Boss damage"), L.T("Ennemis", "Enemies"), "float", 0.2, 5, 0.05, Unit: "×"),
        new(Gs + "bossHealthFactor", L.T("Santé des boss", "Boss health"), L.T("Ennemis", "Enemies"), "float", 0.2, 5, 0.05, Unit: "×"),
        new(Gs + "pacifyAllEnemies", L.T("Ennemis pacifiques", "Peaceful enemies"), L.T("Ennemis", "Enemies"), "bool", Help: L.T("N'attaquent que s'ils sont attaqués (hors boss).", "Only attack when attacked (except bosses).")),
        new(Gs + "tamingStartleRepercussion", L.T("Échec d'apprivoisement", "Taming failure"), L.T("Ennemis", "Enemies"), "enum", Options: ["KeepProgress", "LoseSomeProgress", "LoseAllProgress"]),

        new(Gs + "dayTimeDuration", L.T("Durée du jour", "Day length"), L.T("Monde", "World"), "duration", 2, 60, 1, Unit: "min"),
        new(Gs + "nightTimeDuration", L.T("Durée de la nuit", "Night length"), L.T("Monde", "World"), "duration", 2, 60, 1, Unit: "min"),
        new(Gs + "weatherFrequency", L.T("Fréquence de la météo", "Weather frequency"), L.T("Monde", "World"), "enum", Options: ["Disabled", "Rare", "Normal", "Often"]),
        new(Gs + "curseModifier", L.T("Malédiction du Voile", "Shroud curse"), L.T("Monde", "World"), "enum", Options: ["Easy", "Normal", "Hard"], Help: L.T("Easy désactive la malédiction.", "Easy turns the curse off.")),
        new(Gs + "flameLevelFogRemoval", L.T("Niveau de Flamme pour autels dans le Voile", "Flame level for altars in the Shroud"), L.T("Monde", "World"), "int", 0, 20, 1),
    ];

    public static FieldDto[] GroupFields =>
    [
        new("name", L.T("Nom", "Name"), L.T("Groupe", "Group"), "string"),
        new("password", L.T("Mot de passe", "Password"), L.T("Groupe", "Group"), "password"),
        new("canKickBan", L.T("Expulser / bannir", "Kick / ban"), L.T("Groupe", "Group"), "bool"),
        new("canAccessInventories", L.T("Accès aux coffres", "Access chests"), L.T("Groupe", "Group"), "bool"),
        new("canEditWorld", L.T("Modifier le monde", "Edit world"), L.T("Groupe", "Group"), "bool"),
        new("canEditBase", L.T("Construire dans les bases", "Build in bases"), L.T("Groupe", "Group"), "bool"),
        new("canExtendBase", L.T("Gérer les autels", "Manage altars"), L.T("Groupe", "Group"), "bool"),
        new("reservedSlots", L.T("Places réservées", "Reserved slots"), L.T("Groupe", "Group"), "int", 0, 16, 1),
    ];

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly Instance _instance;

    public ConfigStore(Instance instance) => _instance = instance;

    private string FilePath => _instance.ConfigPath;

    public long Version => File.Exists(FilePath) ? File.GetLastWriteTimeUtc(FilePath).Ticks : 0;

    private JsonObject LoadRoot() =>
        JsonNode.Parse(File.ReadAllText(FilePath), documentOptions: new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip })?.AsObject()
        ?? throw new InvalidDataException(L.T("enshrouded_server.json est vide.", "enshrouded_server.json is empty."));

    public string ServerName()
    {
        try { return LoadRoot()["name"]?.GetValue<string>() ?? "?"; } catch { return "?"; }
    }

    public int QueryPort()
    {
        try { return LoadRoot()["queryPort"]?.GetValue<int>() ?? 15637; } catch { return 15637; }
    }

    /// <summary>Schema, with any unknown enum value found in the file added to its options.</summary>
    public SchemaDto Schema()
    {
        JsonObject? root = null;
        try { root = LoadRoot(); } catch { }
        var fields = Fields.Select(f =>
        {
            if (f.Type != "enum" || root == null) return f;
            var current = Get(root, f.Key)?.ToString();
            return current != null && !f.Options!.Contains(current) ? f with { Options = [.. f.Options!, current] } : f;
        }).ToArray();
        return new SchemaDto(fields, GroupFields);
    }

    public ConfigDto Read()
    {
        var root = LoadRoot();
        var values = new JsonObject();
        foreach (var f in Fields)
        {
            var node = Get(root, f.Key);
            values[f.Key] = f.Type == "duration" && node != null
                ? JsonValue.Create(Math.Round(node.GetValue<double>() / NsPerMinute, 2))
                : node?.DeepClone();
        }
        values["userGroups"] = root["userGroups"]?.DeepClone() ?? new JsonArray();
        return new ConfigDto(values, Version);
    }

    public ApplyResultDto Validate(JsonObject values, out JsonObject newRoot)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        newRoot = LoadRoot();

        foreach (var f in Fields)
        {
            if (!values.TryGetPropertyValue(f.Key, out var v)) continue;
            try
            {
                var node = Convert(f, v, errors, warnings);
                if (node != null) Set(newRoot, f.Key, node);
            }
            catch (Exception)
            {
                errors.Add(L.T($"{f.Label} : valeur invalide.", $"{f.Label}: invalid value."));
            }
        }

        if (values.TryGetPropertyValue("userGroups", out var g))
        {
            if (g is not JsonArray groups) errors.Add(L.T("Groupes : format invalide.", "Groups: invalid format."));
            else
            {
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var cleaned = new JsonArray();
                foreach (var item in groups)
                {
                    if (item is not JsonObject grp) { errors.Add(L.T("Groupes : format invalide.", "Groups: invalid format.")); continue; }
                    var name = grp["name"]?.ToString()?.Trim() ?? "";
                    if (name == "") errors.Add(L.T("Groupes : chaque groupe doit avoir un nom.", "Groups: every group needs a name."));
                    else if (!names.Add(name)) errors.Add(L.T($"Groupes : le nom « {name} » est utilisé deux fois.", $"Groups: the name “{name}” is used twice."));
                    if (string.IsNullOrEmpty(grp["password"]?.ToString()))
                        warnings.Add(L.T($"Groupe « {name} » : sans mot de passe, n'importe qui peut rejoindre avec ces droits.", $"Group “{name}”: without a password, anyone can join with these rights."));
                    var copy = grp.DeepClone().AsObject(); // keeps unknown keys of the group
                    foreach (var gf in GroupFields)
                    {
                        var node = Convert(gf with { Label = L.T($"Groupe « {name} » / {gf.Label}", $"Group “{name}” / {gf.Label}") }, copy[gf.Key], errors, warnings);
                        if (node != null) copy[gf.Key] = node;
                    }
                    copy["name"] = name;
                    cleaned.Add(copy);
                }
                newRoot["userGroups"] = cleaned;
            }
        }

        return new ApplyResultDto(errors.Count == 0, errors.ToArray(), warnings.ToArray());
    }

    private static JsonNode? Convert(FieldDto f, JsonNode? v, List<string> errors, List<string> warnings)
    {
        if (v == null)
        {
            if (f.Type is "string" or "password") return JsonValue.Create("");
            errors.Add(L.T($"{f.Label} : valeur manquante.", $"{f.Label}: missing value."));
            return null;
        }

        void CheckRange(double d)
        {
            if (f.Min is { } min && d < min || f.Max is { } max && d > max)
                warnings.Add(L.T($"{f.Label} = {d.ToString(CultureInfo.InvariantCulture)} est hors de la plage officielle ({f.Min}–{f.Max}).", $"{f.Label} = {d.ToString(CultureInfo.InvariantCulture)} is outside the official range ({f.Min}–{f.Max})."));
        }

        switch (f.Type)
        {
            case "string":
            case "password":
                return JsonValue.Create(v.ToString());
            case "bool":
                return JsonValue.Create(v.GetValue<bool>());
            case "int":
            {
                var d = ToDouble(v);
                if (d != Math.Floor(d)) { errors.Add(L.T($"{f.Label} : nombre entier attendu.", $"{f.Label}: whole number expected.")); return null; }
                if (f.Key is "slotCount" && (d < 1 || d > 16)) { errors.Add(L.T($"{f.Label} : entre 1 et 16.", $"{f.Label}: between 1 and 16.")); return null; }
                if (f.Key is "queryPort" && (d < 1 || d > 65535)) { errors.Add(L.T($"{f.Label} : port invalide.", $"{f.Label}: invalid port.")); return null; }
                if (d < 0) { errors.Add(L.T($"{f.Label} : ne peut pas être négatif.", $"{f.Label}: cannot be negative.")); return null; }
                CheckRange(d);
                return JsonValue.Create((long)d);
            }
            case "float":
            {
                var d = ToDouble(v);
                if (d < 0 || double.IsNaN(d) || double.IsInfinity(d)) { errors.Add(L.T($"{f.Label} : ne peut pas être négatif.", $"{f.Label}: cannot be negative.")); return null; }
                CheckRange(d);
                return JsonValue.Create(Math.Round(d, 6));
            }
            case "duration":
            {
                var minutes = ToDouble(v);
                if (minutes <= 0) { errors.Add(L.T($"{f.Label} : doit être supérieur à 0.", $"{f.Label}: must be greater than 0.")); return null; }
                CheckRange(minutes);
                return JsonValue.Create((long)Math.Round(minutes * NsPerMinute));
            }
            case "enum":
            {
                var s = v.ToString();
                if (!f.Options!.Contains(s)) warnings.Add(L.T($"{f.Label} : valeur « {s} » non documentée.", $"{f.Label}: undocumented value “{s}”."));
                return JsonValue.Create(s);
            }
            case "tags":
            {
                if (v is not JsonArray arr) { errors.Add(L.T($"{f.Label} : liste attendue.", $"{f.Label}: list expected.")); return null; }
                return new JsonArray(arr.Select(t => (JsonNode?)JsonValue.Create(t?.ToString())).ToArray());
            }
        }
        return null;
    }

    private static double ToDouble(JsonNode v) =>
        v is JsonValue jv && jv.TryGetValue<double>(out var d) ? d
            : double.Parse(v.ToString().Replace(',', '.'), CultureInfo.InvariantCulture);

    /// <summary>Writes the file atomically. The caller has stopped the server and made a backup.</summary>
    public void Write(JsonObject root)
    {
        var json = root.ToJsonString(WriteOptions);
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, json, new UTF8Encoding(false));
        File.Move(tmp, FilePath, overwrite: true);
    }

    private static JsonNode? Get(JsonObject root, string key)
    {
        JsonNode? node = root;
        foreach (var part in key.Split('.')) node = node?[part];
        return node;
    }

    private static void Set(JsonObject root, string key, JsonNode value)
    {
        var parts = key.Split('.');
        var obj = root;
        foreach (var part in parts[..^1])
        {
            if (obj[part] is not JsonObject child) obj[part] = child = new JsonObject();
            obj = child;
        }
        obj[parts[^1]] = value;
    }
}
