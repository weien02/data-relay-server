using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace StressTest
{
    internal static class Report
    {
        /// <summary>Prints the run summary. Returns true if the run should be treated as a failure.</summary>
        public static bool Print(List<VirtualClient> all, List<VirtualClient> live, VerificationResult verification, Config config, LoadWindow loadWindow)
        {
            ClientStats total = Aggregate(live);
            long lagMax = 0;
            foreach (VirtualClient client in live)
            {
                if (client.stats.lagMax > lagMax)
                {
                    lagMax = client.stats.lagMax;
                }
            }

            StringBuilder output = new StringBuilder();
            output.AppendLine();
            Section(output, "connections");
            Line(output, "clients requested", all.Count.ToString(CultureInfo.InvariantCulture));
            Line(output, "clients logged in", live.Count.ToString(CultureInfo.InvariantCulture));
            int withEntity = 0;
            foreach (VirtualClient client in live)
            {
                if (client.HasEntity)
                {
                    withEntity++;
                }
            }
            Line(output, "avatars confirmed", withEntity.ToString(CultureInfo.InvariantCulture));
            foreach (VirtualClient client in all)
            {
                if (client.FailureReason != null)
                {
                    Line(output, "  client " + client.Index + " failed", client.FailureReason);
                }
            }

            Section(output, "traffic during the " + loadWindow.seconds.ToString("0.#", CultureInfo.InvariantCulture) + "s load window");
            Line(output, "datagrams received", loadWindow.datagramsIn.ToString("N0", CultureInfo.InvariantCulture)
                + "  (" + loadWindow.PerSecond(loadWindow.datagramsIn).ToString("N0", CultureInfo.InvariantCulture) + "/s)");
            Line(output, "datagrams sent", loadWindow.datagramsOut.ToString("N0", CultureInfo.InvariantCulture)
                + "  (" + loadWindow.PerSecond(loadWindow.datagramsOut).ToString("N0", CultureInfo.InvariantCulture) + "/s)");
            Line(output, "bytes received", Bytes(loadWindow.bytesIn) + "  (" + Bytes((long)loadWindow.PerSecond(loadWindow.bytesIn)) + "/s)");
            Line(output, "bytes sent", Bytes(loadWindow.bytesOut) + "  (" + Bytes((long)loadWindow.PerSecond(loadWindow.bytesOut)) + "/s)");
            Line(output, "state updates published", loadWindow.stateUpdates.ToString("N0", CultureInfo.InvariantCulture));
            Line(output, "entity data packets in", loadWindow.entityData.ToString("N0", CultureInfo.InvariantCulture));

            Section(output, "sync frames");
            double observedRate = loadWindow.seconds > 0 ? loadWindow.maxSyncFramesForOneClient / loadWindow.seconds : 0;
            Line(output, "frames during load", loadWindow.maxSyncFramesForOneClient.ToString("N0", CultureInfo.InvariantCulture)
                + "  (for a client present the whole window)");
            Line(output, "observed sync rate", observedRate.ToString("0.0", CultureInfo.InvariantCulture) + " frames/s"
                + "  (compare against the server's --sync-rate)");
            Line(output, "frame number gaps", total.syncFrameGapCount.ToString("N0", CultureInfo.InvariantCulture)
                + (total.syncFrameGapCount > 0 ? "  (" + total.syncFrameGapTotal + " frames skipped in total)" : ""));
            Line(output, "snapshots on join", total.joinRepairs.ToString("N0", CultureInfo.InvariantCulture)
                + " of " + live.Count.ToString(CultureInfo.InvariantCulture) + " clients");
            Line(output, "mid-run repairs", (total.syncLostRequests - total.joinRepairs).ToString("N0", CultureInfo.InvariantCulture)
                + "  (a gap in frame numbers is the only thing that triggers one)");
            Line(output, "repairs answered", total.syncLostResponses.ToString("N0", CultureInfo.InvariantCulture));
            Line(output, "backup authority asks", total.backupAuthorityRequests.ToString("N0", CultureInfo.InvariantCulture)
                + (total.backupAuthorityRequests > 0 ? "  (the server dropped at least one session mid-run)" : ""));

            Section(output, "staleness while under load");
            output.AppendLine("  how far behind the owner each received sample was, in updates");
            if (total.lagSamples > 0)
            {
                Line(output, "samples", total.lagSamples.ToString("N0", CultureInfo.InvariantCulture));
                Line(output, "mean", ((double)total.lagSum / total.lagSamples).ToString("0.00", CultureInfo.InvariantCulture) + " updates behind");
                Line(output, "worst", lagMax.ToString("N0", CultureInfo.InvariantCulture) + " updates behind");
                output.AppendLine();
                string[] labels = BucketLabels();
                for (int i = 0; i < ClientStats.LagBucketCount; i++)
                {
                    if (total.lagBuckets[i] == 0)
                    {
                        continue;
                    }
                    double share = (double)total.lagBuckets[i] / total.lagSamples;
                    output.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "    {0,-10} {1,12:N0}  {2,6:0.0}%  {3}",
                        labels[i], total.lagBuckets[i], share * 100.0, Bar(share)));
                }
            }
            else
            {
                output.AppendLine("  no samples were received");
            }

            Section(output, "faults seen live");
            Line(output, "impossible states", total.corruptSamples.ToString("N0", CultureInfo.InvariantCulture)
                + "  (a value combination the owner never published)");
            Line(output, "misattributed states", total.misattributedSamples.ToString("N0", CultureInfo.InvariantCulture)
                + "  (another player's state under this entity)");
            Line(output, "out of order updates", total.regressedSamples.ToString("N0", CultureInfo.InvariantCulture)
                + "  (an older update applied after a newer one)");
            Line(output, "data before creation", total.dataBeforeCreation.ToString("N0", CultureInfo.InvariantCulture)
                + "  (dropped, as the real client does)");
            Line(output, "unparseable packets", (total.parseFailures + total.unknownDataStore).ToString("N0", CultureInfo.InvariantCulture));

            Section(output, "cross check after quiesce");
            output.AppendLine("  every observer against every other player's real state");
            Line(output, "pairs checked", verification.pairsChecked.ToString("N0", CultureInfo.InvariantCulture));
            Line(output, "correct", verification.converged.ToString("N0", CultureInfo.InvariantCulture)
                + "  (" + Percent(verification.converged, verification.pairsChecked) + ")");
            Line(output, "stale forever", verification.diverged.ToString("N0", CultureInfo.InvariantCulture)
                + (verification.diverged > 0
                    ? "  (up to " + verification.divergedSequenceShortfallMax + " updates behind, mean "
                        + ((double)verification.divergedSequenceShortfallTotal / verification.diverged).ToString("0.0", CultureInfo.InvariantCulture) + ")"
                    : ""));
            Line(output, "never received data", verification.noDataAtAll.ToString("N0", CultureInfo.InvariantCulture));
            Line(output, "avatar never seen", verification.missingEntity.ToString("N0", CultureInfo.InvariantCulture));
            Line(output, "wrong final state", verification.corruptFinalState.ToString("N0", CultureInfo.InvariantCulture));
            Line(output, "wrong owner", verification.authorityMismatch.ToString("N0", CultureInfo.InvariantCulture));
            output.AppendLine();
            output.AppendLine("  write-once field (respawn position), which the server never resends:");
            Line(output, "  correct", verification.respawnConverged.ToString("N0", CultureInfo.InvariantCulture));
            Line(output, "  wrong value", verification.respawnMismatch.ToString("N0", CultureInfo.InvariantCulture));
            Line(output, "  lost, unrecoverable", verification.respawnMissingLostUpdate.ToString("N0", CultureInfo.InvariantCulture));
            Line(output, "  missed by joining late", verification.respawnMissingJoinedLate.ToString("N0", CultureInfo.InvariantCulture)
                + "  (written before the observer arrived, and the join snapshot did not carry it)");

            if (verification.samples.Count > 0)
            {
                Section(output, "examples");
                foreach (string sample in verification.samples)
                {
                    output.AppendLine("  - " + sample);
                }
            }

            long hardFailures = verification.diverged + verification.missingEntity + verification.noDataAtAll
                + verification.corruptFinalState + verification.authorityMismatch + verification.respawnMismatch
                + verification.respawnMissingLostUpdate
                + total.corruptSamples + total.misattributedSamples + total.regressedSamples
                + (total.parseFailures + total.unknownDataStore);
            bool setupIncomplete = live.Count < all.Count || withEntity < live.Count;

            Section(output, "verdict");
            if (hardFailures == 0 && !setupIncomplete)
            {
                output.AppendLine("  PASS   every client's view of every other client matched the truth.");
            }
            else
            {
                output.AppendLine("  FAIL   " + hardFailures.ToString("N0", CultureInfo.InvariantCulture)
                    + " inconsistencies across " + verification.pairsChecked.ToString("N0", CultureInfo.InvariantCulture) + " pairs"
                    + (setupIncomplete ? ", and not every client got as far as owning an avatar" : "") + ".");
            }
            if (verification.respawnMissingJoinedLate > 0)
            {
                output.AppendLine("  NOTE   " + verification.respawnMissingJoinedLate.ToString("N0", CultureInfo.InvariantCulture)
                    + " pairs never learned a write-once value that was written before the observer joined.");
                output.AppendLine("         Dirty-only sync sends a value only in the frame it changes, so anything not");
                output.AppendLine("         covered by the join snapshot is gone for good.");
            }
            long midRunRepairs = total.syncLostRequests - total.joinRepairs;
            if (midRunRepairs > 0)
            {
                output.AppendLine("  NOTE   " + midRunRepairs.ToString("N0", CultureInfo.InvariantCulture)
                    + " mid-run repairs ran. Each is a full resend that also healed any earlier loss for");
                output.AppendLine("         that client, so the divergence figures above are a lower bound.");
            }

            Console.Write(output.ToString());
            return hardFailures != 0 || setupIncomplete;
        }

        public static ClientStats Aggregate(List<VirtualClient> live)
        {
            ClientStats total = new ClientStats();
            foreach (VirtualClient client in live)
            {
                ClientStats stats = client.stats;
                total.datagramsReceived += stats.datagramsReceived;
                total.datagramsSent += stats.datagramsSent;
                total.bytesReceived += stats.bytesReceived;
                total.bytesSent += stats.bytesSent;
                total.syncFrameBeginCount += stats.syncFrameBeginCount;
                total.syncFrameGapCount += stats.syncFrameGapCount;
                total.syncFrameGapTotal += stats.syncFrameGapTotal;
                total.syncLostRequests += stats.syncLostRequests;
                total.joinRepairs += stats.joinRepairs;
                total.syncLostResponses += stats.syncLostResponses;
                total.entityCreationsReceived += stats.entityCreationsReceived;
                total.entityDeletionsReceived += stats.entityDeletionsReceived;
                total.entityDataReceived += stats.entityDataReceived;
                total.dataBeforeCreation += stats.dataBeforeCreation;
                total.unknownDataStore += stats.unknownDataStore;
                total.parseFailures += stats.parseFailures;
                total.backupAuthorityRequests += stats.backupAuthorityRequests;
                total.stateUpdatesSent += stats.stateUpdatesSent;
                total.corruptSamples += stats.corruptSamples;
                total.misattributedSamples += stats.misattributedSamples;
                total.regressedSamples += stats.regressedSamples;
                total.lagSamples += stats.lagSamples;
                total.lagSum += stats.lagSum;
                for (int i = 0; i < ClientStats.LagBucketCount; i++)
                {
                    total.lagBuckets[i] += stats.lagBuckets[i];
                }
            }
            return total;
        }

        private static string[] BucketLabels()
        {
            string[] labels = new string[ClientStats.LagBucketCount];
            labels[0] = "0";
            int low = 1;
            for (int i = 1; i < ClientStats.LagBucketCount; i++)
            {
                int high = low * 2 - 1;
                if (i == ClientStats.LagBucketCount - 1)
                {
                    labels[i] = low + "+";
                }
                else
                {
                    labels[i] = low == high ? low.ToString(CultureInfo.InvariantCulture) : low + "-" + high;
                }
                low *= 2;
            }
            return labels;
        }

        private static string Bar(double share)
        {
            int width = (int)Math.Round(share * 40.0);
            return new string('#', Math.Max(width, share > 0 ? 1 : 0));
        }

        private static void Section(StringBuilder output, string title)
        {
            output.AppendLine();
            output.AppendLine(title);
            output.AppendLine(new string('-', Math.Max(title.Length, 40)));
        }

        private static void Line(StringBuilder output, string label, string value)
        {
            output.AppendLine("  " + label.PadRight(26) + value);
        }

        private static string Rate(long count, double seconds)
        {
            if (seconds <= 0)
            {
                return "0";
            }
            return (count / seconds).ToString("N0", CultureInfo.InvariantCulture);
        }

        private static string Percent(long part, long whole)
        {
            if (whole == 0)
            {
                return "n/a";
            }
            return (100.0 * part / whole).ToString("0.0", CultureInfo.InvariantCulture) + "%";
        }

        private static string Bytes(long value)
        {
            if (value >= 1024L * 1024L * 1024L)
            {
                return (value / (1024.0 * 1024.0 * 1024.0)).ToString("0.00", CultureInfo.InvariantCulture) + " GiB";
            }
            if (value >= 1024L * 1024L)
            {
                return (value / (1024.0 * 1024.0)).ToString("0.00", CultureInfo.InvariantCulture) + " MiB";
            }
            if (value >= 1024L)
            {
                return (value / 1024.0).ToString("0.00", CultureInfo.InvariantCulture) + " KiB";
            }
            return value + " B";
        }

        public static void WriteJson(string path, List<VirtualClient> all, List<VirtualClient> live, VerificationResult verification, Config config, LoadWindow loadWindow)
        {
            ClientStats total = Aggregate(live);
            StringBuilder json = new StringBuilder();
            json.AppendLine("{");
            json.AppendLine("  \"config\": {");
            json.AppendLine("    \"clients\": " + config.clientCount + ",");
            json.AppendLine("    \"loadSeconds\": " + config.loadSeconds.ToString(CultureInfo.InvariantCulture) + ",");
            json.AppendLine("    \"quiesceSeconds\": " + config.quiesceSeconds.ToString(CultureInfo.InvariantCulture) + ",");
            json.AppendLine("    \"publishEverySyncFrames\": " + config.publishEverySyncFrames + ",");
            json.AppendLine("    \"host\": \"" + config.serverAddress + "\",");
            json.AppendLine("    \"port\": " + config.serverPort + ",");
            json.AppendLine("    \"clientIdBase\": " + config.clientIDBase);
            json.AppendLine("  },");
            json.AppendLine("  \"connections\": { \"requested\": " + all.Count + ", \"loggedIn\": " + live.Count + " },");
            json.AppendLine("  \"traffic\": {");
            json.AppendLine("    \"datagramsReceived\": " + total.datagramsReceived + ",");
            json.AppendLine("    \"datagramsSent\": " + total.datagramsSent + ",");
            json.AppendLine("    \"bytesReceived\": " + total.bytesReceived + ",");
            json.AppendLine("    \"bytesSent\": " + total.bytesSent + ",");
            json.AppendLine("    \"stateUpdatesSent\": " + total.stateUpdatesSent + ",");
            json.AppendLine("    \"entityDataReceived\": " + total.entityDataReceived);
            json.AppendLine("  },");
            json.AppendLine("  \"syncFrames\": {");
            json.AppendLine("    \"totalFrameBegins\": " + total.syncFrameBeginCount + ",");
            json.AppendLine("    \"framesForOneClientDuringLoad\": " + loadWindow.maxSyncFramesForOneClient + ",");
            json.AppendLine("    \"gapCount\": " + total.syncFrameGapCount + ",");
            json.AppendLine("    \"framesSkipped\": " + total.syncFrameGapTotal + ",");
            json.AppendLine("    \"syncLostRequests\": " + total.syncLostRequests + ",");
            json.AppendLine("    \"joinRepairs\": " + total.joinRepairs + ",");
            json.AppendLine("    \"syncLostResponses\": " + total.syncLostResponses + ",");
            json.AppendLine("    \"backupAuthorityRequests\": " + total.backupAuthorityRequests);
            json.AppendLine("  },");
            json.AppendLine("  \"lag\": {");
            json.AppendLine("    \"samples\": " + total.lagSamples + ",");
            json.AppendLine("    \"sum\": " + total.lagSum + ",");
            json.Append("    \"buckets\": [");
            for (int i = 0; i < ClientStats.LagBucketCount; i++)
            {
                json.Append(i > 0 ? ", " : "").Append(total.lagBuckets[i]);
            }
            json.AppendLine("]");
            json.AppendLine("  },");
            json.AppendLine("  \"liveFaults\": {");
            json.AppendLine("    \"impossibleStates\": " + total.corruptSamples + ",");
            json.AppendLine("    \"misattributedStates\": " + total.misattributedSamples + ",");
            json.AppendLine("    \"outOfOrderUpdates\": " + total.regressedSamples + ",");
            json.AppendLine("    \"dataBeforeCreation\": " + total.dataBeforeCreation + ",");
            json.AppendLine("    \"unparseablePackets\": " + (total.parseFailures + total.unknownDataStore));
            json.AppendLine("  },");
            json.AppendLine("  \"crossCheck\": {");
            json.AppendLine("    \"pairsChecked\": " + verification.pairsChecked + ",");
            json.AppendLine("    \"correct\": " + verification.converged + ",");
            json.AppendLine("    \"staleForever\": " + verification.diverged + ",");
            json.AppendLine("    \"staleWorstShortfall\": " + verification.divergedSequenceShortfallMax + ",");
            json.AppendLine("    \"neverReceivedData\": " + verification.noDataAtAll + ",");
            json.AppendLine("    \"avatarNeverSeen\": " + verification.missingEntity + ",");
            json.AppendLine("    \"wrongFinalState\": " + verification.corruptFinalState + ",");
            json.AppendLine("    \"wrongOwner\": " + verification.authorityMismatch + ",");
            json.AppendLine("    \"respawnCorrect\": " + verification.respawnConverged + ",");
            json.AppendLine("    \"respawnWrongValue\": " + verification.respawnMismatch + ",");
            json.AppendLine("    \"respawnLost\": " + verification.respawnMissingLostUpdate + ",");
            json.AppendLine("    \"respawnMissedByJoiningLate\": " + verification.respawnMissingJoinedLate);
            json.AppendLine("  }");
            json.AppendLine("}");
            EnsureDirectoryExists(path);
            File.WriteAllText(path, json.ToString());
        }

        /// <summary>
        /// Creates the directory a report is about to be written into. Called both here and at
        /// startup: a run is minutes of load, and losing it to a missing directory at the very
        /// last step is the one failure this tool must not have.
        /// </summary>
        public static void EnsureDirectoryExists(string path)
        {
            string directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }
    }
}
