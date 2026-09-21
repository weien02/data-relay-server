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

            List<ReconnectEpisode> episodes = CollectEpisodes(all);
            if (episodes.Count > 0)
            {
                PrintReconnection(output, episodes, config);
            }

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
            int episodesFailed = 0;
            foreach (ReconnectEpisode episode in episodes)
            {
                if (!episode.Recovered)
                {
                    episodesFailed++;
                }
            }
            if (episodesFailed > 0)
            {
                output.AppendLine("  NOTE   " + episodesFailed.ToString("N0", CultureInfo.InvariantCulture)
                    + " of " + episodes.Count.ToString("N0", CultureInfo.InvariantCulture)
                    + " reconnection episodes never recovered. Those clients are still counted in the");
                output.AppendLine("         cross check above, so some of the divergence it reports belongs to them");
                output.AppendLine("         rather than to ordinary packet loss. See the reconnection section for which.");
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

        public static List<ReconnectEpisode> CollectEpisodes(List<VirtualClient> all)
        {
            List<ReconnectEpisode> episodes = new List<ReconnectEpisode>();
            foreach (VirtualClient client in all)
            {
                episodes.AddRange(client.episodes);
            }
            return episodes;
        }

        /// <summary>
        /// The reconnection results.
        ///
        /// Episodes are split by whether the client stayed away long enough for the server to
        /// drop its session, because those are two different code paths with nothing in common:
        /// under the threshold the server never learns anything happened and the return is pure
        /// sync-loss repair, over it the session is dropped, an authority backup is asked for,
        /// and the return has to go through login again.
        /// </summary>
        private static void PrintReconnection(StringBuilder output, List<ReconnectEpisode> episodes, Config config)
        {
            Section(output, "reconnection");
            output.AppendLine("  a killed client's time to get its view of everyone else back");
            Line(output, "episodes", episodes.Count.ToString(CultureInfo.InvariantCulture));
            Line(output, "seed", config.killSeed.ToString(CultureInfo.InvariantCulture)
                + "  (pass --kill-seed " + config.killSeed + " to repeat this run)");
            Line(output, "server drop threshold", config.serverDisconnectThresholdSeconds.ToString("0.00", CultureInfo.InvariantCulture) + "s"
                + "  (Configurations.DisconnectThresholdSeconds)");

            int recovered = 0;
            foreach (ReconnectEpisode episode in episodes)
            {
                if (episode.Recovered)
                {
                    recovered++;
                }
            }
            Line(output, "recovered", recovered.ToString(CultureInfo.InvariantCulture)
                + " of " + episodes.Count.ToString(CultureInfo.InvariantCulture)
                + "  (" + Percent(recovered, episodes.Count) + ")");

            PrintEpisodeGroup(output, episodes, false, "stayed away less than the threshold (the server never noticed)");
            PrintEpisodeGroup(output, episodes, true, "stayed away past the threshold (the server dropped the session)");

            output.AppendLine();
            output.AppendLine("  every episode:");
            foreach (ReconnectEpisode episode in episodes)
            {
                output.AppendLine("    player " + episode.playerID
                    + "  " + episode.mode.ToString().ToLowerInvariant()
                    + "  away " + (episode.awaySecondsRequested).ToString("0.0", CultureInfo.InvariantCulture) + "s"
                    + (episode.crossedServerThreshold ? " (over)" : " (under)"));
                if (episode.Recovered)
                {
                    output.AppendLine("      back in " + episode.RecoveryMs.ToString("0", CultureInfo.InvariantCulture) + " ms"
                        + ", outage " + episode.OutageMs.ToString("0", CultureInfo.InvariantCulture) + " ms"
                        + ", " + episode.targetsTotal + " players to re-learn"
                        + ", " + episode.entityDataDuringRecovery.ToString("N0", CultureInfo.InvariantCulture) + " data packets"
                        + ", " + Bytes(episode.bytesDuringRecovery));
                }
                else
                {
                    output.AppendLine("      " + episode.outcome);
                    if (episode.stillStalePlayers.Count > 0)
                    {
                        output.AppendLine("      still stale: player " + string.Join(", ", episode.stillStalePlayers));
                    }
                }
                if (episode.serverNoticedAtMs >= 0 && episode.killedAtMs >= 0)
                {
                    output.AppendLine("      server declared it dead after "
                        + (episode.serverNoticedAtMs - episode.killedAtMs).ToString("0", CultureInfo.InvariantCulture) + " ms");
                }
                if (episode.mode == KillMode.Rejoin && episode.loginResponseAtMs >= 0 && !episode.loginSaidRecoverEntities)
                {
                    output.AppendLine("      the server did not offer its avatar back on login");
                }
            }
        }

        private static void PrintEpisodeGroup(StringBuilder output, List<ReconnectEpisode> episodes, bool crossedThreshold, string title)
        {
            List<double> recoveryMs = new List<double>();
            List<double> roundTripMs = new List<double>();
            List<double> outageMs = new List<double>();
            long dataPackets = 0;
            long bytes = 0;
            int total = 0;
            int failed = 0;
            foreach (ReconnectEpisode episode in episodes)
            {
                if (episode.crossedServerThreshold != crossedThreshold)
                {
                    continue;
                }
                total++;
                if (!episode.Recovered)
                {
                    failed++;
                    continue;
                }
                recoveryMs.Add(episode.RecoveryMs);
                outageMs.Add(episode.OutageMs);
                if (episode.SnapshotRoundTripMs >= 0)
                {
                    roundTripMs.Add(episode.SnapshotRoundTripMs);
                }
                dataPackets += episode.entityDataDuringRecovery;
                bytes += episode.bytesDuringRecovery;
            }
            if (total == 0)
            {
                return;
            }

            output.AppendLine();
            output.AppendLine("  " + title + ": " + total + " episode" + (total == 1 ? "" : "s"));
            if (failed > 0)
            {
                output.AppendLine("    " + failed + " never recovered");
            }
            if (recoveryMs.Count == 0)
            {
                return;
            }
            Line(output, "  back in", Spread(recoveryMs) + "  (resumed to view correct again)");
            Line(output, "  outage", Spread(outageMs) + "  (killed to view correct again)");
            if (roundTripMs.Count > 0)
            {
                Line(output, "  snapshot round trip", Spread(roundTripMs)
                    + "  (asked to answered; the report's \"one RTT\")");
            }
            Line(output, "  recovery traffic", (dataPackets / (double)recoveryMs.Count).ToString("0", CultureInfo.InvariantCulture)
                + " data packets, " + Bytes(bytes / recoveryMs.Count) + " per episode");
        }

        /// <summary>
        /// Reports a distribution rather than a mean. A recovery that usually takes 40 ms and
        /// occasionally takes two seconds is a different system from one that always takes 200,
        /// and an average cannot tell them apart.
        /// </summary>
        private static string Spread(List<double> values)
        {
            values.Sort();
            double median = Percentile(values, 0.5);
            double p95 = Percentile(values, 0.95);
            return "min " + values[0].ToString("0", CultureInfo.InvariantCulture)
                + "  median " + median.ToString("0", CultureInfo.InvariantCulture)
                + "  p95 " + p95.ToString("0", CultureInfo.InvariantCulture)
                + "  max " + values[values.Count - 1].ToString("0", CultureInfo.InvariantCulture) + " ms";
        }

        private static double Percentile(List<double> sorted, double fraction)
        {
            if (sorted.Count == 1)
            {
                return sorted[0];
            }
            double position = fraction * (sorted.Count - 1);
            int low = (int)Math.Floor(position);
            int high = (int)Math.Ceiling(position);
            if (low == high)
            {
                return sorted[low];
            }
            return sorted[low] + (sorted[high] - sorted[low]) * (position - low);
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
                total.blackoutDatagramsDropped += stats.blackoutDatagramsDropped;
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
            json.AppendLine("  },");
            WriteReconnectionJson(json, CollectEpisodes(all), config);
            json.AppendLine("}");
            EnsureDirectoryExists(path);
            File.WriteAllText(path, json.ToString());
        }

        /// <summary>
        /// Every episode as a row, with each timestamp kept separately rather than pre-reduced,
        /// so the intervals can be recut afterwards without rerunning the load.
        /// </summary>
        private static void WriteReconnectionJson(StringBuilder json, List<ReconnectEpisode> episodes, Config config)
        {
            json.AppendLine("  \"reconnection\": {");
            json.AppendLine("    \"killCount\": " + config.killCount + ",");
            json.AppendLine("    \"killMode\": \"" + config.killMode.ToString().ToLowerInvariant() + "\",");
            json.AppendLine("    \"killSeed\": " + config.killSeed + ",");
            json.AppendLine("    \"awayMinSeconds\": " + Number(config.killForMinSeconds) + ",");
            json.AppendLine("    \"awayMaxSeconds\": " + Number(config.killForMaxSeconds) + ",");
            json.AppendLine("    \"recoverTimeoutSeconds\": " + Number(config.recoverTimeoutSeconds) + ",");
            json.AppendLine("    \"serverDisconnectThresholdSeconds\": " + Number(config.serverDisconnectThresholdSeconds) + ",");
            json.Append("    \"episodes\": [");
            for (int i = 0; i < episodes.Count; i++)
            {
                ReconnectEpisode episode = episodes[i];
                json.AppendLine(i > 0 ? "," : "");
                json.AppendLine("      {");
                json.AppendLine("        \"clientIndex\": " + episode.clientIndex + ",");
                json.AppendLine("        \"playerId\": " + episode.playerID + ",");
                json.AppendLine("        \"mode\": \"" + episode.mode.ToString().ToLowerInvariant() + "\",");
                json.AppendLine("        \"awaySecondsRequested\": " + Number(episode.awaySecondsRequested) + ",");
                json.AppendLine("        \"crossedServerThreshold\": " + (episode.crossedServerThreshold ? "true" : "false") + ",");
                json.AppendLine("        \"recovered\": " + (episode.Recovered ? "true" : "false") + ",");
                json.AppendLine("        \"outcome\": \"" + Escape(episode.outcome) + "\",");
                json.AppendLine("        \"killedAtMs\": " + Number(episode.killedAtMs) + ",");
                json.AppendLine("        \"serverNoticedAtMs\": " + Number(episode.serverNoticedAtMs) + ",");
                json.AppendLine("        \"resumedAtMs\": " + Number(episode.resumedAtMs) + ",");
                json.AppendLine("        \"loginResponseAtMs\": " + Number(episode.loginResponseAtMs) + ",");
                json.AppendLine("        \"syncLostRequestAtMs\": " + Number(episode.syncLostRequestAtMs) + ",");
                json.AppendLine("        \"syncLostResponseAtMs\": " + Number(episode.syncLostResponseAtMs) + ",");
                json.AppendLine("        \"viewRestoredAtMs\": " + Number(episode.viewRestoredAtMs) + ",");
                json.AppendLine("        \"recoveryMs\": " + Number(episode.RecoveryMs) + ",");
                json.AppendLine("        \"outageMs\": " + Number(episode.OutageMs) + ",");
                json.AppendLine("        \"snapshotRoundTripMs\": " + Number(episode.SnapshotRoundTripMs) + ",");
                json.AppendLine("        \"playersToRelearn\": " + episode.targetsTotal + ",");
                json.AppendLine("        \"playersStillStale\": " + episode.targetsOutstandingAtEnd + ",");
                json.AppendLine("        \"stillStalePlayerIds\": [" + string.Join(", ", episode.stillStalePlayers) + "],");
                json.AppendLine("        \"loginOfferedEntitiesBack\": " + (episode.loginSaidRecoverEntities ? "true" : "false") + ",");
                json.AppendLine("        \"playerIdChanged\": " + (episode.playerIDChanged ? "true" : "false") + ",");
                json.AppendLine("        \"entityDataPacketsDuringRecovery\": " + episode.entityDataDuringRecovery + ",");
                json.AppendLine("        \"bytesDuringRecovery\": " + episode.bytesDuringRecovery);
                json.Append("      }");
            }
            json.AppendLine(episodes.Count > 0 ? "" : "");
            json.AppendLine("    ]");
            json.AppendLine("  }");
        }

        private static string Number(double value)
        {
            // -1 is the "never happened" marker throughout the episode record; null says that
            // in JSON rather than leaving a sentinel for a reader to trip over.
            if (value < 0)
            {
                return "null";
            }
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "";
            }
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
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
