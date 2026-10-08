using System;
using System.Collections.Generic;
using System.Globalization;

namespace Ecanakli.UIDirector.Tests
{
    // One request line of Navigator.Describe.
    internal sealed class InvariantSnapshotRecord
    {
        public int RequestId;
        public string ViewId;
        public ViewPhase Phase;
        public int Slot;
        public bool ClosePending;
        public bool BeingReplaced;
        public string LayerId;

        // Index in the layer list, bottom first.
        public int Position;

        public override string ToString()
        {
            return "#" + RequestId + " " + ViewId + " " + Phase + " slot " + Slot
                + (ClosePending ? " close pending" : string.Empty)
                + (BeingReplaced ? " being replaced" : string.Empty);
        }
    }

    // One layer block of Navigator.Describe.
    internal sealed class InvariantSnapshotLayer
    {
        public string Id;
        public int Requests;

        // Queued operations; the one that runs is not counted.
        public int Operations;
        public bool Running;
        public readonly List<InvariantSnapshotRecord> Records = new List<InvariantSnapshotRecord>();
    }

    // One shield holder line of Navigator.Describe.
    internal sealed class InvariantSnapshotHolder
    {
        public int HolderId;
        public ShieldHolderKind Kind;

        // Zero for an input hold.
        public int RequestId;
    }

    // The navigator state as far as the outside can see it: Describe is the only structured read-out.
    internal sealed class InvariantSnapshot
    {
        public string State = string.Empty;
        public int ShieldCount;
        public readonly List<InvariantSnapshotLayer> Layers = new List<InvariantSnapshotLayer>();
        public readonly List<InvariantSnapshotHolder> Holders = new List<InvariantSnapshotHolder>();
        public readonly List<string> KeptIds = new List<string>();

        public InvariantSnapshotLayer Layer(string id)
        {
            for (var i = 0; i < Layers.Count; i++)
            {
                if (string.Equals(Layers[i].Id, id, StringComparison.Ordinal))
                    return Layers[i];
            }

            return null;
        }

        public InvariantSnapshotRecord Find(int requestId)
        {
            for (var i = 0; i < Layers.Count; i++)
            {
                var records = Layers[i].Records;
                for (var j = 0; j < records.Count; j++)
                {
                    if (records[j].RequestId == requestId)
                        return records[j];
                }
            }

            return null;
        }

        public int TotalRecords
        {
            get
            {
                var total = 0;
                for (var i = 0; i < Layers.Count; i++)
                    total += Layers[i].Records.Count;
                return total;
            }
        }

        // Reads the text Navigator.Describe writes; unknown lines are ignored, a malformed known line throws.
        public static InvariantSnapshot Parse(string text)
        {
            var snapshot = new InvariantSnapshot();
            var lines = text.Split('\n');

            InvariantSnapshotLayer layer = null;
            var inShield = false;

            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i].TrimEnd('\r');
                if (line.Length == 0)
                    continue;

                if (line.StartsWith("  #", StringComparison.Ordinal))
                {
                    if (inShield)
                        snapshot.Holders.Add(ParseHolder(line));
                    else if (layer != null)
                        layer.Records.Add(ParseRecord(line, layer));
                    else
                        throw new FormatException("A request or holder line outside a block: '" + line + "'.");

                    continue;
                }

                layer = null;
                inShield = false;

                if (line.StartsWith("navigator: ", StringComparison.Ordinal))
                {
                    snapshot.State = line.Substring("navigator: ".Length);
                }
                else if (line.StartsWith("layer ", StringComparison.Ordinal))
                {
                    layer = ParseLayer(line);
                    snapshot.Layers.Add(layer);
                }
                else if (line.StartsWith("shield: ", StringComparison.Ordinal))
                {
                    snapshot.ShieldCount = ParseInt(line.Substring("shield: ".Length), line);
                    inShield = true;
                }
                else if (line.StartsWith("kept instances: ", StringComparison.Ordinal))
                {
                    var tokens = Split(line);
                    for (var t = 3; t < tokens.Length; t++)
                        snapshot.KeptIds.Add(tokens[t]);
                }
            }

            return snapshot;
        }

        // layer <id> requests <n> operations <m> running|idle
        private static InvariantSnapshotLayer ParseLayer(string line)
        {
            var tokens = Split(line);
            if (tokens.Length < 7 || tokens[2] != "requests" || tokens[4] != "operations")
                throw new FormatException("Unexpected layer line: '" + line + "'.");

            return new InvariantSnapshotLayer
            {
                Id = tokens[1],
                Requests = ParseInt(tokens[3], line),
                Operations = ParseInt(tokens[5], line),
                Running = tokens[6] == "running"
            };
        }

        // #<id> <viewId> <Phase> slot <n> [close pending: <reason>] [being replaced]
        private static InvariantSnapshotRecord ParseRecord(string line, InvariantSnapshotLayer layer)
        {
            var tokens = Split(line);
            if (tokens.Length < 5 || tokens[0][0] != '#' || tokens[3] != "slot")
                throw new FormatException("Unexpected request line: '" + line + "'.");

            ViewPhase phase;
            if (!Enum.TryParse(tokens[2], out phase))
                throw new FormatException("Unknown phase in: '" + line + "'.");

            return new InvariantSnapshotRecord
            {
                RequestId = ParseInt(tokens[0].Substring(1), line),
                ViewId = tokens[1],
                Phase = phase,
                Slot = ParseInt(tokens[4], line),
                ClosePending = tokens.Length > 5 && tokens[5] == "close",
                BeingReplaced = line.Contains(" being replaced"),
                LayerId = layer.Id,
                Position = layer.Records.Count
            };
        }

        // #<id> <Kind> [request <id>] ...
        private static InvariantSnapshotHolder ParseHolder(string line)
        {
            var tokens = Split(line);
            if (tokens.Length < 2 || tokens[0][0] != '#')
                throw new FormatException("Unexpected holder line: '" + line + "'.");

            ShieldHolderKind kind;
            if (!Enum.TryParse(tokens[1], out kind))
                throw new FormatException("Unknown holder kind in: '" + line + "'.");

            var requestId = 0;
            if (tokens.Length > 3 && tokens[2] == "request")
                requestId = ParseInt(tokens[3], line);

            return new InvariantSnapshotHolder
            {
                HolderId = ParseInt(tokens[0].Substring(1), line),
                Kind = kind,
                RequestId = requestId
            };
        }

        private static string[] Split(string line)
        {
            return line.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        }

        private static int ParseInt(string token, string line)
        {
            int value;
            if (!int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                throw new FormatException("Not a number '" + token + "' in: '" + line + "'.");

            return value;
        }
    }
}
