namespace DeskScene;

/// <summary>Applies a profile: missing-device check → displays → audio → "keep changes?" with automatic revert.</summary>
static class Switcher
{
    static bool busy;

    public static async Task Apply(Profile profile)
    {
        if (busy) return;
        busy = true;
        var app = App.Current;
        try
        {
            var display = profile.Display;
            var missing = DisplayService.FindMissing(display);
            var action = profile.Options.MissingDevice ?? app.Store.Settings.MissingDevice;
            while (missing.Count > 0 && action == MissingAction.Ask)
            {
                var choice = PromptWindow.Ask($"A device is missing: {Names(missing)}",
                    $"It may be turned off or unplugged. \"{profile.Name}\" needs it.", ["Retry", "Apply without it", "Cancel"]);
                if (choice == 0) missing = DisplayService.FindMissing(display);
                else action = choice == 1 ? MissingAction.ApplyWithout : MissingAction.Cancel;
            }
            if (missing.Count > 0)
            {
                if (action == MissingAction.Cancel)
                {
                    app.Notify($"\"{profile.Name}\" not applied", $"Missing: {Names(missing)}");
                    return;
                }
                display = DisplayService.Without(display, missing.Select(m => m.Id).ToHashSet());
            }

            var before = Profile.Capture("Previous setup");
            var alreadyActive = before.Signature() == profile.Signature();
            DisplayService.Apply(display);
            var missingAudio = await AudioService.ApplyAsync(profile.Audio);

            var settings = app.Store.Settings;
            if (!alreadyActive && settings.ConfirmChanges && profile.Options.ConfirmChanges &&
                PromptWindow.Ask("Keep these display settings?", $"Switched to \"{profile.Name}\".",
                    ["Keep changes", "Revert"], countdown: settings.RevertSeconds, timeoutResult: 1) != 0)
            {
                DisplayService.Apply(before.Display);
                await AudioService.ApplyAsync(before.Audio);
                app.Notify("Reverted", "Your previous setup was restored.");
                return;
            }

            app.Notify($"Switched to \"{profile.Name}\"",
                missingAudio.Count > 0 ? $"Audio device not found: {string.Join(", ", missingAudio)}" : profile.Summary);
        }
        catch (Exception e)
        {
            PromptWindow.Ask($"Couldn't apply \"{profile.Name}\"", e.Message, ["OK"]);
        }
        finally
        {
            busy = false;
            app.Refresh();
        }
    }

    static string Names(IEnumerable<Monitor> monitors) => string.Join(", ", monitors.Select(m => m.Name));
}
