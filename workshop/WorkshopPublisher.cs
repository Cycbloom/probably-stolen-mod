using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Text.Json;
using Steamworks;

// Uses the game's installed Steamworks.NET SDK and the signed-in Steam client.
// https://partner.steamgames.com/doc/api/ISteamUGC
public static class WorkshopPublisher
{
    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetDllDirectory(string path);
    private static readonly AppId_t App = new AppId_t(4348910);
    private static readonly PublishedFileId_t Item = new PublishedFileId_t(3840982832);

    private static void Require(bool success, string operation)
    {
        if (!success) throw new InvalidOperationException(operation + " failed.");
    }

    private static T Wait<T>(SteamAPICall_t call, UGCUpdateHandle_t? update) where T : struct
    {
        bool done = false, failed = false;
        T result = default(T);
        using (var callback = CallResult<T>.Create((T value, bool ioFailure) => {
            result = value; failed = ioFailure; done = true;
        }))
        {
            callback.Set(call);
            var clock = Stopwatch.StartNew();
            long lastProgress = -10;
            while (!done)
            {
                SteamAPI.RunCallbacks();
                if (clock.Elapsed.TotalSeconds > 600) throw new TimeoutException("Steam request timed out; verify server state before retrying.");
                if (update.HasValue && clock.ElapsedMilliseconds / 1000 - lastProgress >= 10)
                {
                    ulong processed, total;
                    var status = SteamUGC.GetItemUpdateProgress(update.Value, out processed, out total);
                    Console.WriteLine("Progress: " + status + " " + processed + "/" + total);
                    lastProgress = clock.ElapsedMilliseconds / 1000;
                }
                if (!done) Thread.Sleep(100);
            }
        }
        Require(!failed, "Steam callback transport");
        return result;
    }

    private static SteamUGCDetails_t Query(string language, string output)
    {
        var query = SteamUGC.CreateQueryUGCDetailsRequest(new[] { Item }, 1);
        try
        {
            Require(SteamUGC.SetLanguage(query, language), "SetLanguage");
            Require(SteamUGC.SetReturnLongDescription(query, true), "SetReturnLongDescription");
            Require(SteamUGC.SetAllowCachedResponse(query, 0), "SetAllowCachedResponse");
            var result = Wait<SteamUGCQueryCompleted_t>(SteamUGC.SendQueryUGCRequest(query), null);
            Require(result.m_eResult == EResult.k_EResultOK && result.m_unNumResultsReturned == 1, "Query " + language + ": " + result.m_eResult);
            SteamUGCDetails_t details;
            Require(SteamUGC.GetQueryUGCResult(query, 0, out details), "GetQueryUGCResult");
            Require(details.m_eResult == EResult.k_EResultOK && details.m_nConsumerAppID == App, "Item identity/result");
            var tags = new List<string>();
            for (uint i = 0; i < SteamUGC.GetQueryUGCNumTags(query, 0); i++)
            {
                string tag;
                Require(SteamUGC.GetQueryUGCTag(query, 0, i, out tag, 256), "GetQueryUGCTag");
                tags.Add(tag);
            }
            File.WriteAllText(output, JsonSerializer.Serialize(new {
                itemId = Item.ToString(), language, title = details.m_rgchTitle,
                description = details.m_rgchDescription, tags,
                visibility = details.m_eVisibility.ToString(), updated = details.m_rtimeUpdated,
                fileSize = details.m_nFileSize, result = details.m_eResult.ToString()
            }), new UTF8Encoding(false));
            Console.WriteLine("Queried " + language + ": " + details.m_rgchTitle + "; tags=" + string.Join(", ", tags));
            return details;
        }
        finally { SteamUGC.ReleaseQueryUGCRequest(query); }
    }

    private static string Read(string folder, string name) { return File.ReadAllText(Path.Combine(folder, name), Encoding.UTF8).TrimEnd(); }
    private static string Normalize(string text) { return text.Replace("\r\n", "\n").TrimEnd(); }

    public static int Main(string[] args)
    {
        bool initialized = false;
        try
        {
            // native directory, workshop directory, receipt directory, query|metadata|publish, content directory
            Require(args.Length == 5, "Arguments");
            Require(SetDllDirectory(args[0]), "Native Steam API directory");
            string error;
            var init = SteamAPI.InitEx(out error);
            Require(init == ESteamAPIInitResult.k_ESteamAPIInitResult_OK, "SteamAPI.InitEx: " + init + " " + error);
            initialized = true;
            Directory.CreateDirectory(args[2]);
            var before = Query("english", Path.Combine(args[2], "before-english.json"));
            Query("schinese", Path.Combine(args[2], "before-schinese.json"));
            if (args[3] == "query") return 0;
            Require(before.m_ulSteamIDOwner == SteamUser.GetSteamID().m_SteamID, "Signed-in account must own this Workshop item");
            var config = JsonDocument.Parse(Read(args[1], "workshop.json")).RootElement;
            Require(config.GetProperty("publishedFileId").GetString() == Item.ToString() && config.GetProperty("appId").GetString() == "4348910", "Configured Workshop identity");
            var tags = new List<string>();
            foreach (var tag in config.GetProperty("tags").EnumerateArray()) tags.Add(tag.GetString());
            foreach (string language in new[] { "english", "schinese" })
            {
                string suffix = language == "english" ? "en" : "zh-CN";
                string title = Read(args[1], "title." + suffix + ".txt");
                string description = Read(args[1], "description." + suffix + ".txt");
                var update = SteamUGC.StartItemUpdate(App, Item);
                Require(SteamUGC.SetItemUpdateLanguage(update, language), "SetItemUpdateLanguage");
                Require(SteamUGC.SetItemTitle(update, title), "SetItemTitle");
                Require(SteamUGC.SetItemDescription(update, description), "SetItemDescription");
                if (language == "english")
                {
                    Require(SteamUGC.SetItemTags(update, tags, false), "SetItemTags");
                    if (args[3] == "publish")
                    {
                        Require(Directory.Exists(args[4]), "Content directory");
                        Require(SteamUGC.SetItemContent(update, args[4]), "SetItemContent");
                        Require(SteamUGC.SetItemPreview(update, Path.Combine(Directory.GetParent(args[4]).FullName, "preview.jpg")), "SetItemPreview");
                    }
                }
                string note = language == "english" ? config.GetProperty("changeNote").GetString() : "";
                Console.WriteLine("Submitting " + language + (language == "english" && args[3] == "publish" ? " and content" : " metadata"));
                var result = Wait<SubmitItemUpdateResult_t>(SteamUGC.SubmitItemUpdate(update, note), update);
                File.WriteAllText(Path.Combine(args[2], "submit-" + language + ".json"), JsonSerializer.Serialize(new {
                    itemId = result.m_nPublishedFileId.ToString(), result = result.m_eResult.ToString(),
                    legalAgreementRequired = result.m_bUserNeedsToAcceptWorkshopLegalAgreement
                }), new UTF8Encoding(false));
                Require(result.m_eResult == EResult.k_EResultOK && result.m_nPublishedFileId == Item, "Submit " + language + ": " + result.m_eResult);
                Require(!result.m_bUserNeedsToAcceptWorkshopLegalAgreement, "Workshop agreement requires the owner's action on Steam");
                Console.WriteLine("Submitted " + language + ": OK");
            }
            foreach (string language in new[] { "english", "schinese" })
            {
                var after = Query(language, Path.Combine(args[2], "verified-" + language + ".json"));
                string suffix = language == "english" ? "en" : "zh-CN";
                Require(after.m_rgchTitle == Read(args[1], "title." + suffix + ".txt"), "Published title " + language);
                Require(Normalize(after.m_rgchDescription) == Normalize(Read(args[1], "description." + suffix + ".txt")), "Published description " + language);
                Require(after.m_eVisibility == before.m_eVisibility, "Workshop visibility preserved");
                var actualTags = new HashSet<string>(after.m_rgchTags.Split(','), StringComparer.OrdinalIgnoreCase);
                foreach (string tag in tags) Require(actualTags.Contains(tag), "Published tag " + tag);
            }
            Console.WriteLine("PASS: both localized titles/descriptions, tags and unchanged visibility verified from Steam.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { if (initialized) SteamAPI.Shutdown(); }
    }
}
