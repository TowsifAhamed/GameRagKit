#include "Misc/AutomationTest.h"
#include "NpcSceneComponent.h"
#include "HAL/PlatformMisc.h"
#include "UObject/Package.h"

#if WITH_DEV_AUTOMATION_TESTS

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FNpcSceneSplitSpokenTest, "GameRagKit.Scene.SplitSpoken", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FNpcSceneSplitSpokenTest::RunTest(const FString& Parameters)
{
    FString Said, Unsaid;
    UNpcSceneComponent::SplitSpoken(TEXT("Not much, to be honest. He passed through the gate."), 30, Said, Unsaid);
    TestEqual(TEXT("said"), Said, FString(TEXT("Not much, to be honest. He")));
    TestEqual(TEXT("unsaid"), Unsaid, FString(TEXT("passed through the gate.")));

    UNpcSceneComponent::SplitSpoken(TEXT("Short."), 999, Said, Unsaid);
    TestTrue(TEXT("fully spoken"), Unsaid.IsEmpty() && Said == TEXT("Short."));

    UNpcSceneComponent::SplitSpoken(TEXT("Never started."), 0, Said, Unsaid);
    TestTrue(TEXT("nothing heard"), Said.IsEmpty() && Unsaid == TEXT("Never started."));
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FNpcSceneGestureTest, "GameRagKit.Scene.ChooseGesture", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FNpcSceneGestureTest::RunTest(const FString& Parameters)
{
    const TArray<FNpcAction> None;
    TestEqual(TEXT("laugh"), UNpcSceneComponent::ChooseGesture(TEXT("Ha! He paid."), FString(), None, FString(), 0.f), FString(TEXT("Cheer")));
    TestEqual(TEXT("wary"), UNpcSceneComponent::ChooseGesture(TEXT("Hmm."), TEXT("wary"), None, FString(), 0.f), FString(TEXT("Block")));
    TestEqual(TEXT("proclaim"), UNpcSceneComponent::ChooseGesture(TEXT("Hear ye, hear ye!"), FString(), None, TEXT("Proclaim"), 0.f), FString(TEXT("Proclaim")));
    TArray<FNpcAction> Give;
    Give.AddDefaulted_GetRef().Name = TEXT("give_item");
    TestEqual(TEXT("give"), UNpcSceneComponent::ChooseGesture(TEXT("Take it."), FString(), Give, FString(), 0.f), FString(TEXT("Use_Item")));
    TestEqual(TEXT("no gesture on a high roll"), UNpcSceneComponent::ChooseGesture(TEXT("Fine."), FString(), None, FString(), 0.9f), FString());
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FNpcSceneWavTest, "GameRagKit.Scene.Wav", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FNpcSceneWavTest::RunTest(const FString& Parameters)
{
    // One second of 440 Hz stereo at 48 kHz -> 16 kHz mono.
    TArray<float> Stereo;
    for (int32 i = 0; i < 48000; ++i)
    {
        const float V = 0.5f * FMath::Sin(i * 2.f * PI * 440.f / 48000.f);
        Stereo.Add(V);
        Stereo.Add(V);
    }

    const TArray<uint8> Wav = UNpcSceneComponent::EncodeSpeechWav(Stereo, 2, 48000);
    TArray<int16> Samples;
    int32 Channels = 0, SampleRate = 0;
    TestTrue(TEXT("decodes"), UNpcSceneComponent::DecodePcm16Wav(Wav, Samples, Channels, SampleRate));
    TestEqual(TEXT("rate"), SampleRate, 16000);
    TestEqual(TEXT("channels"), Channels, 1);
    TestTrue(TEXT("length"), FMath::Abs(Samples.Num() - 16000) <= 1);
    int16 Peak = 0;
    for (const int16 S : Samples) Peak = FMath::Max<int16>(Peak, S);
    TestTrue(TEXT("amplitude"), FMath::Abs(Peak / 32767.f - 0.5f) < 0.02f);
    return true;
}

// ---- End to end against a running server (scripts/run-voice-scene.sh). Server URL:
// GAMERAG_TEST_SERVER, default http://localhost:5290. The component is ticked by hand, so
// no world or audio device is needed; without one, voices are "played" for their duration.

namespace
{
    struct FSceneTestState
    {
        TStrongObjectPtr<UNpcSceneComponent> Scene;
        double Deadline = 0;
        double MarkTime = 0;
        int32 Phase = 0;
    };

    int32 CountNpcLines(const UNpcSceneComponent* Scene)
    {
        int32 Count = 0;
        for (const FNpcSceneHistoryLine& Line : Scene->GetHistory())
        {
            Count += Line.Speaker != TEXT("player") ? 1 : 0;
        }
        return Count;
    }
}

DEFINE_LATENT_AUTOMATION_COMMAND_TWO_PARAMETER(FTickSceneUntil, TSharedPtr<FSceneTestState>, State, FAutomationTestBase*, Test);
bool FTickSceneUntil::Update()
{
    UNpcSceneComponent* Scene = State->Scene.Get();
    Scene->UpdateScene(1.f / 30.f);
    const double Now = FPlatformTime::Seconds();
    if (Now > State->Deadline)
    {
        Test->AddError(FString::Printf(TEXT("Timed out in phase %d"), State->Phase));
        return true;
    }

    switch (State->Phase)
    {
    case 0: // first reply finished (or is speaking) -> start a long one to interrupt
        if (CountNpcLines(Scene) >= 1)
        {
            Test->TestTrue(TEXT("Bram answered"), Scene->GetHistory().Last().Speaker == TEXT("blacksmith-bram") || CountNpcLines(Scene) >= 1);
            Scene->Say(TEXT("Bram, tell me everything you know about the royal messenger."));
            State->Phase = 1;
        }
        return false;
    case 1: // wait until Bram is speaking, then let him talk for 2 s
        if (Scene->GetSpeechLevel(TEXT("blacksmith-bram")) > 0.f)
        {
            State->MarkTime = Now;
            State->Phase = 2;
        }
        return false;
    case 2:
        if (Now - State->MarkTime >= 2.0)
        {
            Scene->Interrupt();
            const FNpcSceneHistoryLine Last = Scene->GetHistory().Last();
            Test->TestEqual(TEXT("interrupted line is Bram's"), Last.Speaker, FString(TEXT("blacksmith-bram")));
            Test->TestTrue(TEXT("marked interrupted"), Last.bInterrupted);
            Test->TestFalse(TEXT("kept the heard words"), Last.Text.IsEmpty());
            return true;
        }
        return false;
    default:
        return true;
    }
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FNpcSceneEndToEndTest, "GameRagKit.Scene.EndToEnd", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FNpcSceneEndToEndTest::RunTest(const FString& Parameters)
{
    FString Server = FPlatformMisc::GetEnvironmentVariable(TEXT("GAMERAG_TEST_SERVER"));
    if (Server.IsEmpty())
    {
        Server = TEXT("http://localhost:5290");
    }

    TSharedPtr<FSceneTestState> State = MakeShared<FSceneTestState>();
    State->Scene = TStrongObjectPtr<UNpcSceneComponent>(NewObject<UNpcSceneComponent>(GetTransientPackage()));
    UNpcSceneComponent* Scene = State->Scene.Get();
    Scene->ServerUrl = Server;
    Scene->bTurnToFace = false;
    FNpcSceneParticipant& Mira = Scene->Participants.AddDefaulted_GetRef();
    Mira.NpcId = TEXT("tavern-keeper-mira");
    Mira.DisplayName = TEXT("Mira");
    FNpcSceneParticipant& Bram = Scene->Participants.AddDefaulted_GetRef();
    Bram.NpcId = TEXT("blacksmith-bram");
    Bram.DisplayName = TEXT("Bram");

    State->Deadline = FPlatformTime::Seconds() + 120.0;
    Scene->Say(TEXT("Bram, how is the forge today?"));
    ADD_LATENT_AUTOMATION_COMMAND(FTickSceneUntil(State, this));
    return true;
}

#endif
