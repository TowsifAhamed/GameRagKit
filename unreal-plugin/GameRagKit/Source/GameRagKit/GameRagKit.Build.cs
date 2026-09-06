using UnrealBuildTool;

public class GameRagKit : ModuleRules
{
    public GameRagKit(ReadOnlyTargetRules Target) : base(Target)
    {
        PCHUsage = PCHUsageMode.UseExplicitOrSharedPCHs;

        PublicDependencyModuleNames.AddRange(new[]
        {
            "Core",
            "CoreUObject",
            "Engine",
            "HTTP",
            "Json",
            "JsonUtilities"
        });
    }
}
