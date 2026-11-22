using SNSRModLoader;

[SNSRModLoader("snsr.example.hello", "Hello Multi Version", "1.0.0")]
[TargetVersions(GameVersion.September2018, GameVersion.December2021, GameVersion.March2023, GameVersion.October2025)]
public sealed class HelloMultiVersion : Plugin
{
    public override void Load(PluginContext context) { context.Logger.Info("Hello from " + context.GameVersion + "."); }
}
