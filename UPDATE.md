# Updating Kumori BPM

Kumori BPM is now distributed only as an external ruleset DLL for the official
osu!lazer client. New releases do not contain the old custom osu! executable,
installer, portable archive, or automatic client updater.

## Move from the old custom client

1. Install or open the official osu!lazer client.
2. Download `osu.Game.Rulesets.Kumori.dll` from the
   [latest Kumori release](https://github.com/Lorenso0/Kumori-BPM/releases/latest).
3. In official osu!, open Settings and select **Open osu! folder**.
4. Close osu! completely.
5. Create a `rulesets` directory in that data folder if it does not exist.
6. Copy the downloaded DLL into `rulesets`.
7. Restart official osu! and select **Kumori**.
8. Enable **Show converts** once in song select so osu!standard maps appear.

The DLL does not modify or update the discontinued custom client. After
confirming that the official client sees Kumori, the old application can be
uninstalled separately. Do not delete its data folder unless you have verified
where your maps, skins, scores, and other user data are stored.

## Update an existing DLL installation

1. Close osu! completely.
2. Download the DLL from the latest GitHub Release.
3. Replace `rulesets/osu.Game.Rulesets.Kumori.dll` in the official osu! data
   folder.
4. Restart osu!.

The accompanying `osu.Game.Rulesets.Kumori.dll.sha256` file can be used to
verify the download. Custom rulesets depend on osu!'s ruleset API, so install a
new Kumori release after an official client update if the previous DLL stops
loading.
