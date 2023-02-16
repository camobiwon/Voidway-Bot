using DSharpPlus.SlashCommands;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Voidway_Bot
{
    [SlashCommandGroup("config", "Modify or retrieve configuration values")]
    internal class ConfigCommands : ApplicationCommandModule
    {
        [SlashCommandGroup("misc", "Other unrelated values")]
        private class Miscellaneous : ApplicationCommandModule
        {
            [SlashCommand(nameof(Config.ConfigValues.logDiscordDebug), "Get / set a config value")]
            [SlashRequireVoidwayOwner]
            public Task LogDiscordDebug(InteractionContext ctx, [Option("Value", "The value to assign, or nothing to retrieve the value.")] bool? value = default)
        {
            if (value.HasValue)
            {
                Config.ModifyConfig(cv => cv.logDiscordDebug = value.Value);
                return ctx.CreateResponseAsync("Done!", true);
            }
            
            return ctx.CreateResponseAsync(Config.GetLogDiscordDebug().ToString(), true);
        }
        }


        [SlashCommandGroup("hoist", "hoist hoisthoist. hoist.")]
        private class Hoist : ApplicationCommandModule
        {
            [SlashCommand(nameof(Config.ConfigValues.hoistCharacters), "Get / set a config value")]
            [SlashRequireVoidwayOwner]
            public Task HoistCharacters(InteractionContext ctx, [Option("Value", "The value to assign, or nothing to retrieve the value.")] string value = "")
            {
                if (!string.IsNullOrEmpty(value))
                {
                    Config.ModifyConfig(cv => cv.hoistCharacters = value);
                    return ctx.CreateResponseAsync("Done!", true);
                }

                string hoistChars = "";
                Config.ModifyConfig(cv => hoistChars = cv.hoistCharacters);
                return ctx.CreateResponseAsync("`" + hoistChars + "`", true);
            }

            [SlashCommand(nameof(Config.ConfigValues.hoistServers), "Get / set a config value")]
            [SlashRequireVoidwayOwner]
            public Task HoistServers(InteractionContext ctx,
                                    [Option("index", "List idx to replace, or nothing to read vals. Use a num >= list length to append value")] long? idx = default,
                                    [Option("value", "value to set, or nothing to remove the value at the given index")] long value = 0)
            {
                if (!idx.HasValue)
                {
                    ulong[] summaries = Array.Empty<ulong>();
                    Config.ModifyConfig(cv => summaries = cv.hoistServers);

                    return ctx.CreateResponseAsync($"[\n\t{string.Join("\n\t", summaries)}\n]", true);
                }

                Config.ModifyConfig(cv =>
                {
                    cv.hoistServers = ModifyOrExpand(cv.hoistServers, (int)idx.Value, (ulong)value, value != 0);
                });

                return ctx.CreateResponseAsync("Done!", true);
            }
        }

        [SlashCommandGroup("modio", "Values related to modio announcements")]
        private class ModAnnoucements : ApplicationCommandModule
        {
            [SlashCommand("trimAnnouncementsWithSummaries", "Get / set a config value")]
            [SlashRequireVoidwayOwner]
            public Task AnnouncementCensorSummary(InteractionContext ctx,
                                                  [Option("index", "List idx to replace, or nothing to read vals. Use a num >= list length to append value")] long? idx = default,
                                                  [Option("value", "value to set, or nothing to remove the value at the given index")] string value = "")
            {
                if (!idx.HasValue)
                {
                    string[] summaries = Array.Empty<string>();
                    Config.ModifyConfig(cv => summaries = cv.censorModsWithSummaryContaining);

                    return ctx.CreateResponseAsync($"[\n\t{string.Join("\n\t", summaries)}\n]", true);
                }

                Config.ModifyConfig(cv =>
                {
                    cv.censorModsWithSummaryContaining = ModifyOrExpand(cv.censorModsWithSummaryContaining, (int)idx.Value, value, string.IsNullOrEmpty(value));
                });

                return ctx.CreateResponseAsync("Done!", true);
            }

            [SlashCommand("trimAnnouncementsWithTitles", "Get / set a config value")]
            [SlashRequireVoidwayOwner]
            public Task AnnouncementCensorTitles(InteractionContext ctx,
                                                [Option("index", "List idx to replace, or nothing to read vals. Use a num >= list length to append value")] long? idx = default,
                                                [Option("value", "value to set, if it exists")] string value = "")
            {
                if (!idx.HasValue)
                {
                    string[] titles = Array.Empty<string>();
                    Config.ModifyConfig(cv => titles = cv.censorModsWithTitlesContaining);

                    return ctx.CreateResponseAsync($"[\n\t{string.Join("\n\t", titles)}\n]", true);
                }

                Config.ModifyConfig(cv =>
                {
                    cv.censorModsWithTitlesContaining = ModifyOrExpand(cv.censorModsWithTitlesContaining, (int)idx.Value, value, string.IsNullOrEmpty(value));
                });

                return ctx.CreateResponseAsync("Done!", true);
            }

            [SlashCommand("trimAnnouncementsWithTag", "Get / set a config value")]
            [SlashRequireVoidwayOwner]
            public Task AnnouncementCensorTags(InteractionContext ctx,
                                              [Option("index", "List idx to replace, or nothing to read vals. Use a num >= list length to append value")] long? idx = default,
                                              [Option("value", "value to set, if it exists")] string value = "")
            {
                if (!idx.HasValue)
                {
                    string[] titles = Array.Empty<string>();
                    Config.ModifyConfig(cv => titles = cv.censorModsWithTag);

                    return ctx.CreateResponseAsync($"[\n\t{string.Join("\n\t", titles)}\n]", true);
                }

                Config.ModifyConfig(cv =>
                {
                    cv.censorModsWithTag = ModifyOrExpand(cv.censorModsWithTag, (int)idx.Value, value, string.IsNullOrEmpty(value));
                });

                return ctx.CreateResponseAsync("Done!", true);
            }

            [SlashCommand(nameof(Config.ConfigValues.ignoreTagspamMods), "Get / set a config value")]
            [SlashRequireVoidwayOwner]
            public Task IgnoreTagspamMods(InteractionContext ctx, [Option("Value", "The value to assign, or nothing to retrieve the value.")] bool? value = default)
            {
                if (value.HasValue)
                {
                    Config.ModifyConfig(cv => cv.logDiscordDebug = value.Value);
                    return ctx.CreateResponseAsync("Done!", true);
                }

                return ctx.CreateResponseAsync(Config.GetIgnoreTagspam().ToString(), true);
            }
        }

        [SlashCommandGroup("msgFilter", "Values related to game invite (e.g. Fusion / BLMP) filtering")]
        public class MessageFiltering : ApplicationCommandModule
        {
            [SlashCommand(nameof(Config.ConfigValues.msgFilterServers), "Get / set a config value")]
            [SlashRequireVoidwayOwner]
            public Task FilterServers(InteractionContext ctx, 
                                          [Option("index", "List idx to replace, or nothing to read vals. Use a num >= list length to append value")] long? idx = default,
                                          [Option("value", "value to set, or nothing to remove the value at the given index")] long value = 0)
            {
                if (!idx.HasValue)
                {
                    ulong[] summaries = Array.Empty<ulong>();
                    Config.ModifyConfig(cv => summaries = cv.msgFilterServers);

                    return ctx.CreateResponseAsync($"[\n\t{string.Join("\n\t", summaries)}\n]", true);
                }

                Config.ModifyConfig(cv =>
                {
                    cv.msgFilterServers = ModifyOrExpand(cv.msgFilterServers, (int)idx.Value, (ulong)value, value != 0);
                });

                return ctx.CreateResponseAsync("Done!", true);
            }

            [SlashCommand(nameof(Config.ConfigValues.msgFilterExceptions), "Get / set a config value")]
            [SlashRequireVoidwayOwner]
            public Task FilterExceptions(InteractionContext ctx,
                                          [Option("index", "List idx to replace, or nothing to read vals. Use a num >= list length to append value")] long? idx = default,
                                          [Option("value", "value to set, or nothing to remove the value at the given index")] long value = 0)
            {
                if (!idx.HasValue)
                {
                    ulong[] summaries = Array.Empty<ulong>();
                    Config.ModifyConfig(cv => summaries = cv.msgFilterExceptions);

                    return ctx.CreateResponseAsync($"[\n\t{string.Join("\n\t", summaries)}\n]", true);
                }

                Config.ModifyConfig(cv =>
                {
                    cv.msgFilterExceptions = ModifyOrExpand(cv.msgFilterExceptions, (int)idx.Value, (ulong)value, value != 0);
                });

                return ctx.CreateResponseAsync("Done!", true);
            }

            [SlashCommand(nameof(Config.ConfigValues.sendWhenFilterMessage), "Get / set a config value")]
            [SlashRequireVoidwayOwner]
            public Task ReactionInvites(InteractionContext ctx,
                                        [Option("index", "List idx to replace, or nothing to read vals. Use a num >= list length to append value")] long? idx = default,
                                        [Option("value", "value to set, if it exists")] string value = "")
            {
                if (!idx.HasValue)
                {
                    string[] titles = Array.Empty<string>();
                    Config.ModifyConfig(cv => titles = cv.sendWhenFilterMessage);

                    return ctx.CreateResponseAsync($"[\n\t{string.Join("\n\t", titles)}\n]", true);
                }

                Config.ModifyConfig(cv =>
                {
                    cv.sendWhenFilterMessage = ModifyOrExpand(cv.sendWhenFilterMessage, (int)idx.Value, value, string.IsNullOrEmpty(value));
                });

                return ctx.CreateResponseAsync("Done!", true);
            }


            [SlashCommand(nameof(Config.ConfigValues.msgFilterMessageTimeout), "Get / set a config value")]
            [SlashRequireVoidwayOwner]
            public Task FilterResponseTimeout(InteractionContext ctx, [Option("Value", "The value to assign, or nothing to retrieve the value.")] long? value = default)
            {
                if (value.HasValue)
                {
                    Config.ModifyConfig(cv => cv.msgFilterMessageTimeout = (int)value.Value);
                    return ctx.CreateResponseAsync("Done!", true);
                }

                return ctx.CreateResponseAsync(Config.GetFilterResponseTimeout().ToString(), true);
            }

            [SlashCommand(nameof(Config.ConfigValues.msgFilterMessageStayTime), "Get / set a config value")]
            [SlashRequireVoidwayOwner]
            public Task FilterResponseStayTime(InteractionContext ctx, [Option("Value", "The value to assign, or nothing to retrieve the value.")] long? value = default)
            {
                if (value.HasValue)
                {
                    Config.ModifyConfig(cv => cv.msgFilterMessageStayTime = (int)value.Value);
                    return ctx.CreateResponseAsync("Done!", true);
                }

                return ctx.CreateResponseAsync(Config.GetFilterResponseTimeout().ToString(), true);
            }
        }

        private static T[] ModifyOrExpand<T>(T[] arr, int idx, T value, bool remove)
        {
            if (idx >= arr.Length)
            {
                arr = arr.Append(value).ToArray();
                return arr;
            }

            // THIS IS SO WASTEFUL BRUH 
            if (idx < arr.Length && remove)
            {
                List<T> list = arr.ToList();
                list.RemoveAt(idx);
                return list.ToArray();
            }

            arr[idx] = value;

            return arr;
        }
    }
}
