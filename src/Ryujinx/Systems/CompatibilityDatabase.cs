using Gommon;
using Humanizer;
using nietras.SeparatedValues;
using Ryujinx.Ava.Common.Locale;
using Ryujinx.Common.Logging;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Ryujinx.Ava.Systems
{
    public class CompatibilityDatabase
    {
        static CompatibilityDatabase() => Load();

        public static void Load()
        {
            using Stream csvStream = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream("RyujinxGameCompatibilityList")!;
            csvStream.Position = 0;

            using SepReader reader = Sep.Reader().From(csvStream);
            ColumnIndices columnIndices = new(reader.Header.IndexOf);

            _entries = reader
                .Enumerate(row => new CompatibilityEntry(ref columnIndices, row))
                .OrderBy(it => it.GameName)
                .ToArray();

            Logger.Debug?.Print(LogClass.UI, "Compatibility CSV loaded.", "LoadCompatibility");
        }

        private static CompatibilityEntry[] _entries;

        public static CompatibilityEntry[] Entries
        {
            get
            {
                if (_entries == null)
                    Load();

                return _entries;
            }
        }

        public static CompatibilityEntry Find(string titleId)
            => Entries.FirstOrDefault(x => x.TitleId.HasValue && x.TitleId.Value.EqualsIgnoreCase(titleId));

        public static CompatibilityEntry Find(ulong titleId)
            => Find(titleId.ToString("X16"));
    }

    public class CompatibilityEntry
    {
        public CompatibilityEntry(ref ColumnIndices indices, SepReader.Row row)
        {
            string titleIdRow = ColStr(row[indices.TitleId]);
            TitleId = !string.IsNullOrEmpty(titleIdRow)
                ? titleIdRow
                : default(Optional<string>);

            GameName = ColStr(row[indices.GameName]);
            _nextendo = new(ResolveNextendo);

            Labels = ColStr(row[indices.Labels])
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            Status = ColStr(row[indices.Status]).ToLower() switch
            {
                "playable" => LocaleKeys.CompatibilityListPlayable,
                "ingame" => LocaleKeys.CompatibilityListIngame,
                "menus" => LocaleKeys.CompatibilityListMenus,
                "boots" => LocaleKeys.CompatibilityListBoots,
                "nothing" => LocaleKeys.CompatibilityListNothing,
                _ => null
            };

            if (DateTime.TryParse(ColStr(row[indices.LastUpdated]), out DateTime dt))
                LastUpdated = dt;

            string knownIssuesRow = indices.HasKnownIssues
                ? ColStr(row[indices.KnownIssues])
                : string.Empty;
            KnownIssues = !string.IsNullOrWhiteSpace(knownIssuesRow)
                ? knownIssuesRow
                : BuildKnownIssuesFromLabels(Labels);

            return;

            static string ColStr(SepReader.Col col) => col.ToString().Trim('"');
        }

        public string GameName { get; }
        public Optional<string> TitleId { get; }

        // [Nextendo] Online support on Nextendo Network: the built-in title table first, then
        // the titles of the signed service catalog (loaded once when the list opens).
        public static System.Collections.Generic.Dictionary<string, string> NextendoCatalogVersions { get; set; }
            = new(StringComparer.OrdinalIgnoreCase);

        public bool IsNextendo => _nextendo.Value.Listed;
        public string NextendoBadge => _nextendo.Value.Version.Length > 0 ? "Nextendo · v" + _nextendo.Value.Version : "Nextendo";

        private readonly Lazy<(bool Listed, string Version)> _nextendo;

        private (bool, string) ResolveNextendo()
        {
            if (!TitleId.HasValue)
                return (false, string.Empty);
            string id = TitleId.Value.ToLowerInvariant();
            string builtIn = Ryujinx.Ava.Systems.AppLibrary.ApplicationData.NextendoVersionFor(id);
            // Built-in titles need exactly their version (ApplicationData.IsNextendoVersionOk), like catalog titles.
            if (!string.IsNullOrEmpty(builtIn))
                return (true, builtIn);
            return NextendoCatalogVersions.TryGetValue(id, out string v) ? (true, v ?? string.Empty) : (false, string.Empty);
        }
        public string[] Labels { get; }
        public string KnownIssues { get; }
        public LocaleKeys? Status { get; }

        public LocaleKeys? StatusDescription
            => Status switch
            {
                LocaleKeys.CompatibilityListPlayable => LocaleKeys.CompatibilityListPlayableTooltip,
                LocaleKeys.CompatibilityListIngame => LocaleKeys.CompatibilityListIngameTooltip,
                LocaleKeys.CompatibilityListMenus => LocaleKeys.CompatibilityListMenusTooltip,
                LocaleKeys.CompatibilityListBoots => LocaleKeys.CompatibilityListBootsTooltip,
                LocaleKeys.CompatibilityListNothing => LocaleKeys.CompatibilityListNothingTooltip,
                _ => null
            };

        public DateTime LastUpdated { get; }

        public string LocalizedLastUpdated =>
            LocaleManager.FormatDynamicValue(LocaleKeys.CompatibilityListLastUpdated, LastUpdated.Humanize());

        public string LocalizedStatus => LocaleManager.Instance[Status!.Value];
        public string LocalizedStatusDescription => LocaleManager.Instance[StatusDescription!.Value];
        public string FormattedTitleId => TitleId
            .OrElse(new string(' ', 16));

        public string FormattedIssueLabels => Labels
            .Select(FormatLabelName)
            .Where(x => x != null)
            .JoinToString(", ");

        public string FormattedCompatibilityDetails
        {
            get
            {
                string labels = FormattedIssueLabels;
                if (string.IsNullOrEmpty(labels))
                    return KnownIssues;

                if (string.IsNullOrEmpty(KnownIssues))
                    return labels;

                return labels + " — " + KnownIssues;
            }
        }

        public static string BuildKnownIssuesFromLabels(string[] labels)
        {
            if (labels.Length == 0)
                return "No known glitches recorded for this title.";

            return labels
                .Select(DescribeLabel)
                .Where(x => !string.IsNullOrEmpty(x))
                .JoinToString(" ");
        }

        public override string ToString() =>
            new StringBuilder("CompatibilityEntry: {")
                .Append($"{nameof(GameName)}=\"{GameName}\", ")
                .Append($"{nameof(TitleId)}={TitleId}, ")
                .Append($"{nameof(Labels)}={Labels.FormatCollection(it => $"\"{it}\"", separator: ", ", prefix: "[", suffix: "]")}, ")
                .Append($"{nameof(Status)}=\"{Status}\", ")
                .Append($"{nameof(LastUpdated)}=\"{LastUpdated}\"")
                .Append('}')
                .ToString();

        public static string FormatLabelName(string labelName) => labelName.ToLower() switch
        {
            "audio" => "Audio",
            "bug" => "Bug",
            "cpu" => "CPU",
            "gpu" => "GPU",
            "gui" => "GUI",
            "help wanted" => "Help Wanted",
            "horizon" => "Horizon",
            "invalid" => "Invalid",
            "kernel" => "Kernel",
            "ldn" => "LDN",
            "linux" => "Linux",
            "macos" => "macOS",
            "question" => "Question",
            "windows" => "Windows",
            "graphics-backend:opengl" => "Graphics: OpenGL",
            "graphics-backend:vulkan" => "Graphics: Vulkan",
            "ldn-works" => "LDN Works",
            "ldn-untested" => "LDN Untested",
            "ldn-broken" => "LDN Broken",
            "ldn-partial" => "Partial LDN",
            "nvdec" => "GPU Video Decoding",
            "services" => "HLE Services",
            "services-horizon" => "New HLE Services",
            "slow" => "Runs Slow",
            "crash" => "Crashes",
            "deadlock" => "Deadlock",
            "regression" => "Regression",
            "opengl" => "OpenGL",
            "opengl-backend-bug" => "OpenGL Backend Bug",
            "vulkan-backend-bug" => "Vulkan Backend Bug",
            "mac-bug" => "Mac-specific Problems",
            "amd-vendor-bug" => "AMD GPU Bug",
            "intel-vendor-bug" => "Intel GPU Bug",
            "loader-allocator" => "Loader Allocator",
            "audout" => "AudOut",
            "32-bit" => "32-bit Game",
            "ue4" => "Unreal Engine 4",
            "homebrew" => "Homebrew Content",
            "online-broken" => "Online Broken",
            "online" => "Online Services",
            "needs update" => "Needs Update",
            "incomplete" => "Incomplete Report",
            "demo" => "Demo Build",
            "lan" => "LAN Multiplayer",
            "compat-unverified" => "Unverified Status",
            _ => null
        };

        public static string DescribeLabel(string labelName) => labelName.ToLower() switch
        {
            "audio" => "Audio issues such as silence, crackling, or desync.",
            "audout" => "AudOut service issues affecting sound output.",
            "gpu" => "GPU rendering glitches, corruption, or missing visuals.",
            "nvdec" => "Hardware video decoding (NVDEC) problems; cutscenes or FMV may fail.",
            "crash" => "The game crashes during play or at specific scenes.",
            "deadlock" => "The game can hang or deadlock.",
            "slow" => "Performance is below expected frame rate.",
            "services" => "HLE service emulation issues.",
            "services-horizon" => "New Horizon service stubs are incomplete.",
            "online" => "Online features are affected or untested.",
            "online-broken" => "Online multiplayer or services do not work.",
            "ldn-works" => "Local wireless (LDN) works with limitations.",
            "ldn-untested" => "Local wireless (LDN) has not been verified.",
            "ldn-broken" => "Local wireless (LDN) does not work.",
            "ldn-partial" => "Local wireless (LDN) works only partially.",
            "ldn" => "Local wireless (LDN) related issue.",
            "opengl-backend-bug" => "OpenGL graphics backend bug.",
            "vulkan-backend-bug" => "Vulkan graphics backend bug.",
            "opengl" => "OpenGL-specific problem.",
            "amd-vendor-bug" => "AMD GPU-specific driver or backend issue.",
            "intel-vendor-bug" => "Intel GPU-specific driver or backend issue.",
            "mac-bug" => "macOS-specific problem.",
            "loader-allocator" => "Loader memory allocator issue at startup.",
            "32-bit" => "32-bit title; may need specific CPU settings.",
            "ue4" => "Unreal Engine 4 title with engine-specific bugs.",
            "homebrew" => "Homebrew content; not a commercial release.",
            "regression" => "Previously worked; broke in a newer emulator build.",
            "bug" => "General emulation bug.",
            "cpu" => "CPU emulation issue.",
            "kernel" => "Kernel/HOS emulation issue.",
            "horizon" => "Horizon OS service issue.",
            "gui" => "Emulator UI or overlay issue.",
            "needs update" => "Game or emulator build needs an update for this title.",
            "incomplete" => "Compatibility report is incomplete.",
            "demo" => "Demo build; full game may differ.",
            "lan" => "LAN multiplayer label from the tracker.",
            "compat-unverified" => "No verified status on the tracker; treat as unknown.",
            _ => $"Tracked label: {labelName}."
        };
    }

    public struct ColumnIndices(Func<ReadOnlySpan<char>, int> getIndex)
    {
        private const string TitleIdCol = "\"title_id\"";
        private const string GameNameCol = "\"game_name\"";
        private const string LabelsCol = "\"labels\"";
        private const string StatusCol = "\"status\"";
        private const string LastUpdatedCol = "\"last_updated\"";
        private const string KnownIssuesCol = "\"known_issues\"";

        public readonly int TitleId = getIndex(TitleIdCol);
        public readonly int GameName = getIndex(GameNameCol);
        public readonly int Labels = getIndex(LabelsCol);
        public readonly int Status = getIndex(StatusCol);
        public readonly int LastUpdated = getIndex(LastUpdatedCol);
        public readonly int KnownIssues = TryGetIndex(getIndex, KnownIssuesCol);
        public readonly bool HasKnownIssues = TryGetIndex(getIndex, KnownIssuesCol) >= 0;

        private static int TryGetIndex(Func<ReadOnlySpan<char>, int> getIndex, string column)
        {
            try
            {
                return getIndex(column);
            }
            catch
            {
                return -1;
            }
        }
    }
}
