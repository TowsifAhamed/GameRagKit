#pragma once

#include "CoreMinimal.h"
#include "Components/ActorComponent.h"
#include "Interfaces/IHttpRequest.h"
#include "NpcDialogueComponent.h"
#include "NpcSceneComponent.generated.h"

class UAudioComponent;
class USoundAttenuation;
namespace Audio { class FAudioCaptureSynth; }

/** One NPC taking part in a scene. */
USTRUCT(BlueprintType)
struct FNpcSceneParticipant
{
    GENERATED_BODY()

    /** NPC id on the GameRagKit server (persona id or YAML file name). */
    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "GameRagKit")
    FString NpcId;

    /** How players and other NPCs address this character, e.g. "Mira". */
    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "GameRagKit")
    FString DisplayName;

    /** The NPC in the level. Its voice plays attached to it, and it turns to face whoever it's talking to. */
    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "GameRagKit")
    TObjectPtr<AActor> Actor = nullptr;

    /** Optional attenuation for this NPC's voice (falls back to the component's VoiceAttenuation). */
    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "GameRagKit")
    TObjectPtr<USoundAttenuation> Attenuation = nullptr;

    /** Optional gesture for exclamations (e.g. a town crier's "Proclaim" montage). */
    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "GameRagKit")
    FString SignatureGesture;
};

/** One spoken NPC line in a scene. */
USTRUCT(BlueprintType)
struct FNpcSceneLine
{
    GENERATED_BODY()

    UPROPERTY(BlueprintReadOnly, Category = "GameRagKit")
    int32 Index = 0;

    UPROPERTY(BlueprintReadOnly, Category = "GameRagKit")
    FString NpcId;

    UPROPERTY(BlueprintReadOnly, Category = "GameRagKit")
    FString Text;

    /** "routed" (answering the player) or "reaction" (answering another NPC). */
    UPROPERTY(BlueprintReadOnly, Category = "GameRagKit")
    FString Reason;

    UPROPERTY(BlueprintReadOnly, Category = "GameRagKit")
    FString Mood;

    UPROPERTY(BlueprintReadOnly, Category = "GameRagKit")
    TArray<FNpcAction> Actions;

    /** Suggested gesture: Interact, Cheer, Block, Use_Item, the NPC's signature gesture, or empty. */
    UPROPERTY(BlueprintReadOnly, Category = "GameRagKit")
    FString Gesture;

    /** Who the line is aimed at: "player" or another participant's NPC id. */
    UPROPERTY(BlueprintReadOnly, Category = "GameRagKit")
    FString Addressee = TEXT("player");
};

/** A line of scene history: only what was actually heard. */
USTRUCT(BlueprintType)
struct FNpcSceneHistoryLine
{
    GENERATED_BODY()

    /** "player" or an NPC id. */
    UPROPERTY(BlueprintReadOnly, Category = "GameRagKit")
    FString Speaker;

    UPROPERTY(BlueprintReadOnly, Category = "GameRagKit")
    FString Text;

    /** The speaker was talked over; Text holds just the part that was heard. */
    UPROPERTY(BlueprintReadOnly, Category = "GameRagKit")
    bool bInterrupted = false;
};

DECLARE_DYNAMIC_MULTICAST_DELEGATE_OneParam(FOnNpcSceneTranscript, const FString&, Transcript);
DECLARE_DYNAMIC_MULTICAST_DELEGATE_TwoParams(FOnNpcSceneRouted, const TArray<FString>&, Responders, const FString&, Method);
DECLARE_DYNAMIC_MULTICAST_DELEGATE_OneParam(FOnNpcSceneThinking, const FString&, NpcId);
DECLARE_DYNAMIC_MULTICAST_DELEGATE_OneParam(FOnNpcSceneLineStarted, const FNpcSceneLine&, Line);
DECLARE_DYNAMIC_MULTICAST_DELEGATE_FourParams(FOnNpcSceneLineFinished, const FNpcSceneLine&, Line, bool, bInterrupted, const FString&, Said, const FString&, Unsaid);
DECLARE_DYNAMIC_MULTICAST_DELEGATE_OneParam(FOnNpcSceneError, const FString&, ErrorMessage);

/**
 * Voice conversations with one or more GameRagKit NPCs (the server's /scene API): the
 * player types or speaks, the server works out which NPC(s) should answer, and each reply
 * plays in that NPC's own voice, attached to the NPC actor -- NPCs can answer each other
 * too. The player can talk over an NPC (Interrupt, or just start talking): only the words
 * actually heard are remembered, the NPC keeps what it didn't get to say and can bring it
 * up later, and "go on" hands it the floor back.
 *
 * Voice input needs speech-to-text (whisper.cpp) on the server; spoken replies need NPC
 * voices (Kokoro or Piper). Without them lines still arrive as text. Works with one NPC too.
 */
UCLASS(ClassGroup = (GameRagKit), meta = (BlueprintSpawnableComponent))
class GAMERAGKIT_API UNpcSceneComponent : public UActorComponent
{
    GENERATED_BODY()

public:
    UNpcSceneComponent();

    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "GameRagKit|Configuration")
    FString ServerUrl = TEXT("http://localhost:5280");

    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "GameRagKit|Configuration")
    FString ApiKey;

    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "GameRagKit|Scene")
    TArray<FNpcSceneParticipant> Participants;

    /** Ask the server for spoken replies (needs NPC voices configured on the server). */
    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "GameRagKit|Scene")
    bool bSynthesizeVoices = true;

    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "GameRagKit|Scene", meta = (ClampMin = 1, ClampMax = 6))
    int32 MaxResponders = 2;

    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "GameRagKit|Scene", meta = (ClampMin = 0, ClampMax = 3))
    int32 MaxReactions = 1;

    /** Default attenuation for NPC voices, so they sound like they come from the NPC. */
    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "GameRagKit|Scene")
    TObjectPtr<USoundAttenuation> VoiceAttenuation = nullptr;

    /** Turn each participant's actor toward whoever it is talking or listening to. */
    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "GameRagKit|Body Language")
    bool bTurnToFace = true;

    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "GameRagKit|Body Language")
    float TurnSpeed = 5.f;

    /** Longest single recording, in seconds. */
    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "GameRagKit|Microphone")
    float MaxRecordingSeconds = 20.f;

    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "GameRagKit|Configuration")
    bool bEnableLogging = false;

    /** What the server heard the player say (voice input only). */
    UPROPERTY(BlueprintAssignable, Category = "GameRagKit|Events")
    FOnNpcSceneTranscript OnTranscript;

    /** Which NPC ids will answer, and why (addressed-by-name, llm-router, continue-interrupted, ...). */
    UPROPERTY(BlueprintAssignable, Category = "GameRagKit|Events")
    FOnNpcSceneRouted OnRouted;

    UPROPERTY(BlueprintAssignable, Category = "GameRagKit|Events")
    FOnNpcSceneThinking OnThinking;

    /** An NPC starts saying a line: show the subtitle, play Gesture, apply Actions. */
    UPROPERTY(BlueprintAssignable, Category = "GameRagKit|Events")
    FOnNpcSceneLineStarted OnLineStarted;

    /** A line ended. When interrupted, only Said was heard; the NPC remembers Unsaid. */
    UPROPERTY(BlueprintAssignable, Category = "GameRagKit|Events")
    FOnNpcSceneLineFinished OnLineFinished;

    UPROPERTY(BlueprintAssignable, Category = "GameRagKit|Events")
    FOnNpcSceneError OnSceneError;

    /** Say something (typed) to the NPCs. Talks over anyone currently speaking. */
    UFUNCTION(BlueprintCallable, Category = "GameRagKit|Scene")
    void Say(const FString& Text);

    /** Push-to-talk down: start recording the player (also cuts the NPCs off). */
    UFUNCTION(BlueprintCallable, Category = "GameRagKit|Scene")
    void StartListening();

    /** Push-to-talk up: stop recording and send what was said. */
    UFUNCTION(BlueprintCallable, Category = "GameRagKit|Scene")
    void StopListeningAndSend();

    /** Send already-recorded speech (16-bit PCM WAV, ideally 16 kHz mono). */
    UFUNCTION(BlueprintCallable, Category = "GameRagKit|Scene")
    void SendSpeech(const TArray<uint8>& WavBytes);

    /** Cut the NPCs off: stop the voice mid-line and keep only the heard words in the history. */
    UFUNCTION(BlueprintCallable, Category = "GameRagKit|Scene")
    void Interrupt();

    /** Forget the conversation (history and unsaid words) and stop everything. */
    UFUNCTION(BlueprintCallable, Category = "GameRagKit|Scene")
    void ResetConversation();

    /** 0..1 loudness of what this NPC is saying right now (drive a jaw bone or head nod in the Anim Blueprint). */
    UFUNCTION(BlueprintPure, Category = "GameRagKit|Scene")
    float GetSpeechLevel(const FString& NpcId) const;

    UFUNCTION(BlueprintPure, Category = "GameRagKit|Scene")
    bool IsListening() const { return bListening; }

    UFUNCTION(BlueprintPure, Category = "GameRagKit|Scene")
    TArray<FNpcSceneHistoryLine> GetHistory() const { return History; }

    /** Per-frame work: mic polling, reading the server stream, playback, facing. Called from TickComponent; call it yourself if you drive the component manually. */
    void UpdateScene(float DeltaTime);

    virtual void TickComponent(float DeltaTime, ELevelTick TickType, FActorComponentTickFunction* ThisTickFunction) override;
    virtual void EndPlay(const EEndPlayReason::Type EndPlayReason) override;

    // Conversation rules shared with the web demo and the Unity client.
    static void SplitSpoken(const FString& Text, int32 SpokenChars, FString& OutSaid, FString& OutUnsaid);
    static FString ChooseGesture(const FString& Text, const FString& Mood, const TArray<FNpcAction>& Actions, const FString& Signature, float Roll);
    static TArray<uint8> EncodeSpeechWav(const TArray<float>& Interleaved, int32 NumChannels, int32 SampleRate);
    static bool DecodePcm16Wav(const TArray<uint8>& Wav, TArray<int16>& OutSamples, int32& OutChannels, int32& OutSampleRate);

private:
    struct FBeat
    {
        TMap<int32, FNpcSceneLine> Lines;
        TMap<int32, TArray<uint8>> Clips; // decoded WAV bytes; missing = still coming
        TSet<int32> NoClip;
        bool bWithVoice = true;
        int32 Next = 0;
        bool bDone = false;
        bool bCancelled = false;
    };

    struct FPlaying
    {
        FNpcSceneLine Line;
        int32 ParticipantIndex = INDEX_NONE;
        TWeakObjectPtr<UAudioComponent> Audio;
        double StartedAt = 0;
        float Duration = 0;
        bool bHasAudio = false;
        TArray<float> Envelope; // RMS per 20 ms
    };

    void StartScene(const FString& Message, const TArray<uint8>* Wav);
    void HandleStreamedBytes(void* Ptr, int64& Length);
    void DrainStream();
    void HandleEvent(const TSharedPtr<FJsonObject>& Event);
    void StartLine(const FNpcSceneLine& Line);
    void FinishCurrent();
    void StopCurrentAudio();
    void Remember(const FString& Speaker, const FString& Text, bool bInterrupted);
    void AddUnsaid(const FString& NpcId, const FString& Text);
    int32 FindParticipant(const FString& NpcId) const;
    void UpdateFacing(float DeltaTime);
    void RaiseError(const FString& Message);
    double Now() const;

    TArray<FNpcSceneHistoryLine> History;
    TMap<FString, TPair<FString, int32>> Unsaid; // NpcId -> (text, turns left)

    TSharedPtr<IHttpRequest, ESPMode::ThreadSafe> CurrentRequest;
    int32 RequestId = 0;
    TSharedPtr<FBeat> Beat;
    TOptional<FPlaying> Current;

    // SSE bytes arrive on the HTTP thread; they're parsed on the game thread in Tick.
    FCriticalSection StreamLock;
    FString StreamBuffer;
    int32 StreamRequestId = 0;
    bool bStreamComplete = false;
    bool bStreamSucceeded = false;
    int32 StreamStatus = 0;

    // Shared (not unique) so the generated code can destroy it with the type forward-declared.
    TSharedPtr<Audio::FAudioCaptureSynth> Capture;
    TArray<float> Recorded;
    int32 CaptureChannels = 1;
    int32 CaptureSampleRate = 48000;
    double ListeningSince = 0;
    bool bListening = false;
};
