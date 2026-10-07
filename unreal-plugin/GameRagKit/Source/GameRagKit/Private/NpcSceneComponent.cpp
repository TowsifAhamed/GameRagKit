#include "NpcSceneComponent.h"

#include "AudioCaptureCore.h"
#include "Components/AudioComponent.h"
#include "Dom/JsonObject.h"
#include "Dom/JsonValue.h"
#include "GameFramework/Pawn.h"
#include "GameFramework/PlayerController.h"
#include "HttpModule.h"
#include "Interfaces/IHttpResponse.h"
#include "Internationalization/Regex.h"
#include "Kismet/GameplayStatics.h"
#include "Misc/Base64.h"
#include "Policies/CondensedJsonPrintPolicy.h"
#include "Serialization/JsonSerializer.h"
#include "Sound/SoundWaveProcedural.h"

namespace
{
    constexpr int32 HistoryLimit = 20;
    constexpr int32 UnsaidTurns = 2;
    constexpr int32 UnsaidMaxChars = 400;
    constexpr int32 SpeechSampleRate = 16000;
    constexpr float EnvelopeStep = 0.02f;

    FString ToJson(const TSharedRef<FJsonObject>& Object)
    {
        FString Out;
        const TSharedRef<TJsonWriter<TCHAR, TCondensedJsonPrintPolicy<TCHAR>>> Writer = TJsonWriterFactory<TCHAR, TCondensedJsonPrintPolicy<TCHAR>>::Create(&Out);
        FJsonSerializer::Serialize(Object, Writer);
        return Out;
    }

    FString ToJson(const TArray<TSharedPtr<FJsonValue>>& Array)
    {
        FString Out;
        const TSharedRef<TJsonWriter<TCHAR, TCondensedJsonPrintPolicy<TCHAR>>> Writer = TJsonWriterFactory<TCHAR, TCondensedJsonPrintPolicy<TCHAR>>::Create(&Out);
        FJsonSerializer::Serialize(Array, Writer);
        return Out;
    }

    bool Matches(const FString& Pattern, const FString& Text)
    {
        FRegexMatcher Matcher(FRegexPattern(Pattern, ERegexPatternFlags::CaseInsensitive), Text);
        return Matcher.FindNext();
    }

    void WriteInt32(TArray<uint8>& Out, int32 Value) { Out.Append(reinterpret_cast<const uint8*>(&Value), 4); }
    void WriteInt16(TArray<uint8>& Out, int16 Value) { Out.Append(reinterpret_cast<const uint8*>(&Value), 2); }
    void WriteTag(TArray<uint8>& Out, const char* Tag) { Out.Append(reinterpret_cast<const uint8*>(Tag), 4); }
}

UNpcSceneComponent::UNpcSceneComponent()
{
    PrimaryComponentTick.bCanEverTick = true;
}

// ---------------------------------------------------------------- public API

void UNpcSceneComponent::Say(const FString& Text)
{
    const FString Trimmed = Text.TrimStartAndEnd();
    if (Trimmed.IsEmpty())
    {
        return;
    }

    Interrupt();
    Remember(TEXT("player"), Trimmed, false);
    StartScene(Trimmed, nullptr);
}

void UNpcSceneComponent::StartListening()
{
    if (bListening)
    {
        return;
    }

    Interrupt();
    Capture = MakeShared<Audio::FAudioCaptureSynth>();
    Audio::FCaptureDeviceInfo Info;
    if (!Capture->GetDefaultCaptureDeviceInfo(Info) || !Capture->OpenDefaultStream() || !Capture->StartCapturing())
    {
        Capture.Reset();
        RaiseError(TEXT("Couldn't open the microphone (is the AudioCapture plugin enabled, and mic access allowed?)."));
        return;
    }

    CaptureChannels = FMath::Max(1, Info.InputChannels);
    CaptureSampleRate = Info.PreferredSampleRate > 0 ? Info.PreferredSampleRate : 48000;
    Recorded.Reset();
    ListeningSince = Now();
    bListening = true;
}

void UNpcSceneComponent::StopListeningAndSend()
{
    if (!bListening || !Capture.IsValid())
    {
        return;
    }

    TArray<float> Tail;
    Capture->GetAudioData(Tail);
    Recorded.Append(Tail);
    Capture->StopCapturing();
    Capture.Reset();
    bListening = false;

    const int32 Frames = Recorded.Num() / CaptureChannels;
    if (Frames < CaptureSampleRate * 0.35f)
    {
        RaiseError(TEXT("Recording too short: hold the talk button while speaking."));
        return;
    }

    SendSpeech(EncodeSpeechWav(Recorded, CaptureChannels, CaptureSampleRate));
}

void UNpcSceneComponent::SendSpeech(const TArray<uint8>& WavBytes)
{
    if (WavBytes.Num() <= 44)
    {
        RaiseError(TEXT("No audio to send."));
        return;
    }

    Interrupt();
    StartScene(FString(), &WavBytes);
}

void UNpcSceneComponent::Interrupt()
{
    if (CurrentRequest.IsValid())
    {
        ++RequestId; // late events from the cancelled request are ignored
        CurrentRequest->CancelRequest();
        CurrentRequest.Reset();
    }

    if (!Beat.IsValid() || Beat->bCancelled)
    {
        return;
    }

    Beat->bCancelled = true;
    TArray<int32> Pending;
    Beat->Lines.GetKeys(Pending);
    Pending.Sort();
    for (const int32 Index : Pending)
    {
        if (Index < Beat->Next)
        {
            continue;
        }

        const FNpcSceneLine& Line = Beat->Lines[Index];
        FString Said;
        FString Left = Line.Text;
        const bool bWasPlaying = Current.IsSet() && Current->Line.Index == Index;
        if (bWasPlaying)
        {
            if (!Current->bHasAudio && !Beat->bWithVoice)
            {
                Said = Line.Text; // text-only lines are shown in full when they start
                Left.Reset();
            }
            else
            {
                const float Fraction = FMath::Clamp(static_cast<float>((Now() - Current->StartedAt) / FMath::Max(0.01f, Current->Duration)), 0.f, 1.f);
                SplitSpoken(Line.Text, FMath::RoundToInt(Line.Text.Len() * Fraction), Said, Left);
            }

            StopCurrentAudio();
            OnLineFinished.Broadcast(Line, true, Said, Left);
        }

        if (!Said.IsEmpty())
        {
            Remember(Line.NpcId, Said, true);
        }

        if (!Left.IsEmpty())
        {
            AddUnsaid(Line.NpcId, Left);
        }
    }

    Current.Reset();
}

void UNpcSceneComponent::ResetConversation()
{
    Interrupt();
    Beat.Reset();
    History.Reset();
    Unsaid.Reset();
}

float UNpcSceneComponent::GetSpeechLevel(const FString& NpcId) const
{
    if (!Current.IsSet() || Current->Line.NpcId != NpcId)
    {
        return 0.f;
    }

    if (Current->Envelope.Num() == 0)
    {
        const float T = static_cast<float>(Now());
        return 0.4f + 0.2f * FMath::Sin(T * 5.3f) * FMath::Sin(T * 1.7f);
    }

    const int32 Frame = FMath::Clamp(static_cast<int32>((Now() - Current->StartedAt) / EnvelopeStep), 0, Current->Envelope.Num() - 1);
    return Current->Envelope[Frame];
}

// ---------------------------------------------------------------- request

void UNpcSceneComponent::StartScene(const FString& Message, const TArray<uint8>* Wav)
{
    if (Participants.Num() == 0)
    {
        RaiseError(TEXT("UNpcSceneComponent has no participants."));
        return;
    }

    Beat = MakeShared<FBeat>();
    Beat->bWithVoice = bSynthesizeVoices;

    TArray<TSharedPtr<FJsonValue>> ParticipantsJson;
    for (const FNpcSceneParticipant& P : Participants)
    {
        TSharedRef<FJsonObject> Obj = MakeShared<FJsonObject>();
        Obj->SetStringField(TEXT("npc"), P.NpcId);
        Obj->SetStringField(TEXT("name"), P.DisplayName.IsEmpty() ? P.NpcId : P.DisplayName);
        ParticipantsJson.Add(MakeShared<FJsonValueObject>(Obj));
    }

    TSharedRef<FJsonObject> Settings = MakeShared<FJsonObject>();
    TArray<TSharedPtr<FJsonValue>> HistoryJson;
    for (int32 i = FMath::Max(0, History.Num() - HistoryLimit); i < History.Num(); ++i)
    {
        TSharedRef<FJsonObject> Obj = MakeShared<FJsonObject>();
        Obj->SetStringField(TEXT("speaker"), History[i].Speaker);
        Obj->SetStringField(TEXT("text"), History[i].Text);
        Obj->SetBoolField(TEXT("interrupted"), History[i].bInterrupted);
        HistoryJson.Add(MakeShared<FJsonValueObject>(Obj));
    }
    TArray<TSharedPtr<FJsonValue>> UnsaidJson;
    for (const auto& Pair : Unsaid)
    {
        TSharedRef<FJsonObject> Obj = MakeShared<FJsonObject>();
        Obj->SetStringField(TEXT("speaker"), Pair.Key);
        Obj->SetStringField(TEXT("text"), Pair.Value.Key);
        UnsaidJson.Add(MakeShared<FJsonValueObject>(Obj));
    }
    Settings->SetArrayField(TEXT("history"), HistoryJson);
    Settings->SetArrayField(TEXT("unsaid"), UnsaidJson);
    Settings->SetNumberField(TEXT("maxResponders"), MaxResponders);
    Settings->SetNumberField(TEXT("maxReactions"), MaxReactions);
    Settings->SetBoolField(TEXT("synthesizeReply"), bSynthesizeVoices);

    TSharedRef<IHttpRequest, ESPMode::ThreadSafe> Request = FHttpModule::Get().CreateRequest();
    Request->SetVerb(TEXT("POST"));
    Request->SetHeader(TEXT("X-GameRAG-Protocol"), TEXT("1"));
    if (!ApiKey.IsEmpty())
    {
        Request->SetHeader(TEXT("X-Api-Key"), ApiKey);
    }

    if (Wav != nullptr)
    {
        const FString Boundary = FString::Printf(TEXT("----GameRagKit%08x"), FMath::Rand());
        TArray<uint8> Body;
        auto AppendText = [&Body](const FString& Text)
        {
            const FTCHARToUTF8 Utf8(*Text);
            Body.Append(reinterpret_cast<const uint8*>(Utf8.Get()), Utf8.Length());
        };
        AppendText(FString::Printf(TEXT("--%s\r\nContent-Disposition: form-data; name=\"audio\"; filename=\"speech.wav\"\r\nContent-Type: audio/wav\r\n\r\n"), *Boundary));
        Body.Append(*Wav);
        AppendText(FString::Printf(TEXT("\r\n--%s\r\nContent-Disposition: form-data; name=\"participants\"\r\n\r\n%s"), *Boundary, *ToJson(ParticipantsJson)));
        AppendText(FString::Printf(TEXT("\r\n--%s\r\nContent-Disposition: form-data; name=\"request\"\r\n\r\n%s"), *Boundary, *ToJson(Settings)));
        AppendText(FString::Printf(TEXT("\r\n--%s--\r\n"), *Boundary));
        Request->SetURL(ServerUrl + TEXT("/scene/voice"));
        Request->SetHeader(TEXT("Content-Type"), FString::Printf(TEXT("multipart/form-data; boundary=%s"), *Boundary));
        Request->SetContent(MoveTemp(Body));
    }
    else
    {
        Settings->SetArrayField(TEXT("participants"), ParticipantsJson);
        Settings->SetStringField(TEXT("message"), Message);
        Request->SetURL(ServerUrl + TEXT("/scene/ask"));
        Request->SetHeader(TEXT("Content-Type"), TEXT("application/json"));
        Request->SetContentAsString(ToJson(Settings));
    }

    const int32 ThisRequest = ++RequestId;
    {
        FScopeLock Lock(&StreamLock);
        StreamBuffer.Reset();
        StreamRequestId = ThisRequest;
        bStreamComplete = false;
        bStreamSucceeded = false;
        StreamStatus = 0;
    }

    Request->SetResponseBodyReceiveStreamDelegateV2(FHttpRequestStreamDelegateV2::CreateUObject(this, &UNpcSceneComponent::HandleStreamedBytes));
    TWeakObjectPtr<UNpcSceneComponent> WeakThis(this);
    Request->OnProcessRequestComplete().BindLambda([WeakThis, ThisRequest](FHttpRequestPtr, FHttpResponsePtr Response, bool bSucceeded)
    {
        if (UNpcSceneComponent* Self = WeakThis.Get())
        {
            FScopeLock Lock(&Self->StreamLock);
            if (Self->StreamRequestId == ThisRequest)
            {
                Self->bStreamComplete = true;
                Self->bStreamSucceeded = bSucceeded && Response.IsValid() && Response->GetResponseCode() == 200;
                Self->StreamStatus = Response.IsValid() ? Response->GetResponseCode() : 0;
            }
        }
    });

    CurrentRequest = Request;
    Request->ProcessRequest();
}

void UNpcSceneComponent::HandleStreamedBytes(void* Ptr, int64& Length)
{
    if (Ptr == nullptr || Length <= 0)
    {
        return;
    }

    const FUTF8ToTCHAR Converter(static_cast<const ANSICHAR*>(Ptr), static_cast<int32>(Length));
    FScopeLock Lock(&StreamLock);
    StreamBuffer.AppendChars(Converter.Get(), Converter.Length());
}

void UNpcSceneComponent::DrainStream()
{
    TArray<FString> Lines;
    bool bComplete = false;
    bool bSucceeded = false;
    int32 Status = 0;
    {
        FScopeLock Lock(&StreamLock);
        if (StreamRequestId != RequestId)
        {
            return;
        }

        int32 NewlineIndex;
        while (StreamBuffer.FindChar(TEXT('\n'), NewlineIndex))
        {
            Lines.Add(StreamBuffer.Left(NewlineIndex).TrimEnd());
            StreamBuffer.RemoveAt(0, NewlineIndex + 1);
        }

        if (bStreamComplete)
        {
            if (!StreamBuffer.IsEmpty())
            {
                Lines.Add(StreamBuffer.TrimEnd());
                StreamBuffer.Reset();
            }

            bComplete = true;
            bSucceeded = bStreamSucceeded;
            Status = StreamStatus;
            bStreamComplete = false;
            StreamRequestId = INDEX_NONE;
        }
    }

    for (const FString& Line : Lines)
    {
        if (!Line.StartsWith(TEXT("data:")))
        {
            continue;
        }

        TSharedPtr<FJsonObject> Event;
        const TSharedRef<TJsonReader<>> Reader = TJsonReaderFactory<>::Create(Line.RightChop(5).TrimStartAndEnd());
        if (FJsonSerializer::Deserialize(Reader, Event) && Event.IsValid())
        {
            HandleEvent(Event);
        }
    }

    if (bComplete && Beat.IsValid())
    {
        CurrentRequest.Reset();
        if (!bSucceeded)
        {
            RaiseError(Status >= 400
                ? FString::Printf(TEXT("Scene request failed (HTTP %d) -- does the server have speech-to-text for voice input?"), Status)
                : TEXT("Scene request failed: connection error"));
        }

        Beat->bDone = true;
        // A line whose audio never arrives still gets "spoken" (as text for its duration).
        for (const auto& Pair : Beat->Lines)
        {
            if (!Beat->Clips.Contains(Pair.Key))
            {
                Beat->NoClip.Add(Pair.Key);
            }
        }
    }
}

void UNpcSceneComponent::HandleEvent(const TSharedPtr<FJsonObject>& Event)
{
    FString Type;
    Event->TryGetStringField(TEXT("type"), Type);
    FString Npc;
    Event->TryGetStringField(TEXT("npc"), Npc);
    int32 Index = 0;
    Event->TryGetNumberField(TEXT("index"), Index);

    if (bEnableLogging)
    {
        UE_LOG(LogTemp, Log, TEXT("[GameRagKit] scene event %s %s"), *Type, *Npc);
    }

    if (Type == TEXT("transcript"))
    {
        FString Text;
        Event->TryGetStringField(TEXT("text"), Text);
        Remember(TEXT("player"), Text, false);
        OnTranscript.Broadcast(Text);
    }
    else if (Type == TEXT("routing"))
    {
        TArray<FString> Responders;
        Event->TryGetStringArrayField(TEXT("responders"), Responders);
        FString Method;
        Event->TryGetStringField(TEXT("method"), Method);
        OnRouted.Broadcast(Responders, Method);
    }
    else if (Type == TEXT("thinking"))
    {
        OnThinking.Broadcast(Npc);
    }
    else if (Type == TEXT("turn") && Beat.IsValid() && !Beat->bCancelled)
    {
        FNpcSceneLine Line;
        Line.Index = Index;
        Line.NpcId = Npc;
        Event->TryGetStringField(TEXT("text"), Line.Text);
        Event->TryGetStringField(TEXT("reason"), Line.Reason);
        const TSharedPtr<FJsonObject>* MoodObject;
        if (Event->TryGetObjectField(TEXT("mood"), MoodObject))
        {
            (*MoodObject)->TryGetStringField(TEXT("value"), Line.Mood);
        }

        const TArray<TSharedPtr<FJsonValue>>* ActionsArray;
        if (Event->TryGetArrayField(TEXT("actions"), ActionsArray))
        {
            for (const TSharedPtr<FJsonValue>& Value : *ActionsArray)
            {
                const TSharedPtr<FJsonObject>* ActionObject;
                if (!Value->TryGetObject(ActionObject))
                {
                    continue;
                }

                FNpcAction Action;
                (*ActionObject)->TryGetStringField(TEXT("name"), Action.Name);
                const TSharedPtr<FJsonObject>* Args;
                if ((*ActionObject)->TryGetObjectField(TEXT("args"), Args))
                {
                    for (const auto& Pair : (*Args)->Values)
                    {
                        Action.Args.Add(FString(Pair.Key), Pair.Value->AsString());
                    }
                }

                Line.Actions.Add(Action);
            }
        }

        const int32 ParticipantIndex = FindParticipant(Npc);
        Line.Gesture = ChooseGesture(Line.Text, Line.Mood, Line.Actions,
            ParticipantIndex != INDEX_NONE ? Participants[ParticipantIndex].SignatureGesture : FString(), FMath::FRand());

        // Who it's aimed at: the first other participant it names, else the player.
        int32 Best = MAX_int32;
        for (const FNpcSceneParticipant& P : Participants)
        {
            const FString Name = P.DisplayName.IsEmpty() ? P.NpcId : P.DisplayName;
            if (P.NpcId == Npc || Name.IsEmpty())
            {
                continue;
            }

            FRegexMatcher Matcher(FRegexPattern(FString::Printf(TEXT("\\b%s\\b"), *Name), ERegexPatternFlags::CaseInsensitive), Line.Text);
            if (Matcher.FindNext() && Matcher.GetMatchBeginning() < Best)
            {
                Best = Matcher.GetMatchBeginning();
                Line.Addressee = P.NpcId;
            }
        }

        Beat->Lines.Add(Index, Line);
        if (!bSynthesizeVoices)
        {
            Beat->NoClip.Add(Index);
        }
    }
    else if (Type == TEXT("audio") && Beat.IsValid())
    {
        FString Base64;
        TArray<uint8> Wav;
        if (Event->TryGetStringField(TEXT("wavBase64"), Base64) && !Base64.IsEmpty() && FBase64::Decode(Base64, Wav))
        {
            Beat->Clips.Add(Index, MoveTemp(Wav));
        }
        else
        {
            Beat->NoClip.Add(Index);
        }
    }
    else if (Type == TEXT("error"))
    {
        FString Error;
        Event->TryGetStringField(TEXT("error"), Error);
        RaiseError(Error);
    }
}

// ---------------------------------------------------------------- playback

void UNpcSceneComponent::TickComponent(float DeltaTime, ELevelTick TickType, FActorComponentTickFunction* ThisTickFunction)
{
    Super::TickComponent(DeltaTime, TickType, ThisTickFunction);
    UpdateScene(DeltaTime);
}

void UNpcSceneComponent::UpdateScene(float DeltaTime)
{
    if (bListening && Capture.IsValid())
    {
        TArray<float> Chunk;
        if (Capture->GetAudioData(Chunk))
        {
            Recorded.Append(Chunk);
        }

        if (Now() - ListeningSince >= MaxRecordingSeconds)
        {
            StopListeningAndSend();
        }
    }

    DrainStream();

    if (Beat.IsValid() && !Beat->bCancelled)
    {
        if (Current.IsSet() && Now() - Current->StartedAt >= Current->Duration)
        {
            FinishCurrent();
        }

        if (!Current.IsSet())
        {
            if (const FNpcSceneLine* Next = Beat->Lines.Find(Beat->Next))
            {
                if (Beat->Clips.Contains(Beat->Next) || Beat->NoClip.Contains(Beat->Next))
                {
                    StartLine(*Next);
                }
            }
        }
    }

    UpdateFacing(DeltaTime);
}

void UNpcSceneComponent::StartLine(const FNpcSceneLine& Line)
{
    FPlaying Playing;
    Playing.Line = Line;
    Playing.ParticipantIndex = FindParticipant(Line.NpcId);
    Playing.StartedAt = Now();
    // No audio (text-only, or no voice on the server): give the subtitle reading time.
    Playing.Duration = FMath::Clamp(Line.Text.Len() / 14.f, 1.2f, 8.f);

    TArray<int16> Samples;
    int32 Channels = 0;
    int32 SampleRate = 0;
    if (const TArray<uint8>* Wav = Beat->Clips.Find(Line.Index); Wav && DecodePcm16Wav(*Wav, Samples, Channels, SampleRate) && Samples.Num() > 0)
    {
        Playing.bHasAudio = true;
        Playing.Duration = static_cast<float>(Samples.Num() / Channels) / SampleRate;

        const int32 Window = FMath::Max(1, static_cast<int32>(SampleRate * EnvelopeStep) * Channels);
        for (int32 Start = 0; Start < Samples.Num(); Start += Window)
        {
            double Sum = 0;
            const int32 End = FMath::Min(Start + Window, Samples.Num());
            for (int32 i = Start; i < End; ++i)
            {
                const double V = Samples[i] / 32768.0;
                Sum += V * V;
            }

            Playing.Envelope.Add(FMath::Clamp(static_cast<float>(FMath::Sqrt(Sum / FMath::Max(1, End - Start)) * 4.0), 0.f, 1.f));
        }

        AActor* Actor = Playing.ParticipantIndex != INDEX_NONE ? Participants[Playing.ParticipantIndex].Actor.Get() : nullptr;
        USoundWaveProcedural* Wave = NewObject<USoundWaveProcedural>(this);
        Wave->SetSampleRate(SampleRate);
        Wave->NumChannels = Channels;
        Wave->Duration = Playing.Duration;
        Wave->SoundGroup = SOUNDGROUP_Voice;
        Wave->bLooping = false;
        Wave->QueueAudio(reinterpret_cast<const uint8*>(Samples.GetData()), Samples.Num() * sizeof(int16));

        USoundAttenuation* Attenuation = Playing.ParticipantIndex != INDEX_NONE && Participants[Playing.ParticipantIndex].Attenuation
            ? Participants[Playing.ParticipantIndex].Attenuation.Get()
            : VoiceAttenuation.Get();
        // No world (e.g. headless tests): the line is still "played" for its duration.
        if (GetWorld() != nullptr)
        {
            UAudioComponent* AudioComponent = Actor && Actor->GetRootComponent()
                ? UGameplayStatics::SpawnSoundAttached(Wave, Actor->GetRootComponent(), NAME_None, FVector(0, 0, 160), EAttachLocation::KeepRelativeOffset, true, 1.f, 1.f, 0.f, Attenuation)
                : UGameplayStatics::SpawnSound2D(this, Wave);
            Playing.Audio = AudioComponent;
        }
    }

    Current = MoveTemp(Playing);
    OnLineStarted.Broadcast(Line);
}

void UNpcSceneComponent::FinishCurrent()
{
    const FNpcSceneLine Line = Current->Line;
    StopCurrentAudio();
    Current.Reset();
    ++Beat->Next;

    Remember(Line.NpcId, Line.Text, false);
    if (TPair<FString, int32>* Entry = Unsaid.Find(Line.NpcId))
    {
        // It had its chance to bring up what it was holding back.
        if (--Entry->Value <= 0)
        {
            Unsaid.Remove(Line.NpcId);
        }
    }

    OnLineFinished.Broadcast(Line, false, Line.Text, FString());
}

void UNpcSceneComponent::StopCurrentAudio()
{
    if (Current.IsSet() && Current->Audio.IsValid())
    {
        Current->Audio->Stop();
    }
}

void UNpcSceneComponent::UpdateFacing(float DeltaTime)
{
    if (!bTurnToFace)
    {
        return;
    }

    if (GetWorld() == nullptr)
    {
        return;
    }

    APawn* PlayerPawn = UGameplayStatics::GetPlayerPawn(this, 0);
    const AActor* Speaker = Current.IsSet() && Current->ParticipantIndex != INDEX_NONE ? Participants[Current->ParticipantIndex].Actor.Get() : nullptr;
    for (int32 i = 0; i < Participants.Num(); ++i)
    {
        AActor* Actor = Participants[i].Actor.Get();
        if (!Actor)
        {
            continue;
        }

        const AActor* Target = PlayerPawn;
        if (Speaker == Actor)
        {
            const int32 Addressee = Current->Line.Addressee == TEXT("player") ? INDEX_NONE : FindParticipant(Current->Line.Addressee);
            Target = Addressee != INDEX_NONE && Participants[Addressee].Actor ? Participants[Addressee].Actor.Get() : PlayerPawn;
        }
        else if (Speaker)
        {
            Target = Speaker;
        }

        if (!Target || Target == Actor)
        {
            continue;
        }

        FVector Direction = Target->GetActorLocation() - Actor->GetActorLocation();
        Direction.Z = 0;
        if (Direction.SizeSquared() > 1.f)
        {
            const FRotator Goal(0.f, Direction.Rotation().Yaw, 0.f);
            const FRotator Currently = Actor->GetActorRotation();
            Actor->SetActorRotation(FMath::RInterpTo(Currently, FRotator(Currently.Pitch, Goal.Yaw, Currently.Roll), DeltaTime, TurnSpeed));
        }
    }
}

// ---------------------------------------------------------------- helpers

void UNpcSceneComponent::Remember(const FString& Speaker, const FString& Text, bool bInterrupted)
{
    FNpcSceneHistoryLine Line;
    Line.Speaker = Speaker;
    Line.Text = Text;
    Line.bInterrupted = bInterrupted;
    History.Add(Line);
    if (History.Num() > HistoryLimit)
    {
        History.RemoveAt(0, History.Num() - HistoryLimit);
    }
}

void UNpcSceneComponent::AddUnsaid(const FString& NpcId, const FString& Text)
{
    const TPair<FString, int32>* Existing = Unsaid.Find(NpcId);
    FString Combined = Existing ? Existing->Key + TEXT(" ") + Text : Text;
    if (Combined.Len() > UnsaidMaxChars)
    {
        Combined = Combined.Right(UnsaidMaxChars);
    }

    Unsaid.Add(NpcId, TPair<FString, int32>(Combined, UnsaidTurns));
}

int32 UNpcSceneComponent::FindParticipant(const FString& NpcId) const
{
    return Participants.IndexOfByPredicate([&NpcId](const FNpcSceneParticipant& P) { return P.NpcId.Equals(NpcId, ESearchCase::IgnoreCase); });
}

void UNpcSceneComponent::RaiseError(const FString& Message)
{
    UE_LOG(LogTemp, Warning, TEXT("[GameRagKit] %s"), *Message);
    OnSceneError.Broadcast(Message);
}

double UNpcSceneComponent::Now() const
{
    return FPlatformTime::Seconds();
}

void UNpcSceneComponent::EndPlay(const EEndPlayReason::Type EndPlayReason)
{
    if (Capture.IsValid())
    {
        Capture->AbortCapturing();
        Capture.Reset();
        bListening = false;
    }

    Interrupt();
    Super::EndPlay(EndPlayReason);
}

// ---------------------------------------------------------------- shared rules

void UNpcSceneComponent::SplitSpoken(const FString& Text, int32 SpokenChars, FString& OutSaid, FString& OutUnsaid)
{
    if (SpokenChars >= Text.Len() - 2)
    {
        OutSaid = Text.TrimStartAndEnd();
        OutUnsaid.Reset();
        return;
    }

    int32 Cut = INDEX_NONE;
    if (SpokenChars > 0)
    {
        Cut = Text.Find(TEXT(" "), ESearchCase::CaseSensitive, ESearchDir::FromEnd, FMath::Min(SpokenChars + 1, Text.Len()));
    }

    if (Cut <= 0)
    {
        OutSaid.Reset();
        OutUnsaid = Text.TrimStartAndEnd();
        return;
    }

    OutSaid = Text.Left(Cut).TrimStartAndEnd();
    OutUnsaid = Text.RightChop(Cut).TrimStartAndEnd();
}

FString UNpcSceneComponent::ChooseGesture(const FString& Text, const FString& Mood, const TArray<FNpcAction>& Actions, const FString& Signature, float Roll)
{
    if (Actions.Num() > 0)
    {
        return Matches(TEXT("give|hand|trade|sell|offer|buy"), Actions[0].Name) ? TEXT("Use_Item") : TEXT("Interact");
    }

    if (Matches(TEXT("delight|happy|joy|excit|cheer|amus|glad|thrill|merry"), Mood) || Matches(TEXT("\\b(ha){2,}\\b|\\bha!|\\bhah\\b"), Text))
    {
        return TEXT("Cheer");
    }

    if (Matches(TEXT("wary|suspici|annoy|angry|hostile|defensive|irritat|offend"), Mood))
    {
        return TEXT("Block");
    }

    int32 Exclamations = 0;
    for (const TCHAR C : Text)
    {
        Exclamations += C == TEXT('!') ? 1 : 0;
    }

    if (!Signature.IsEmpty() && (Matches(TEXT("^\\W*(hear ye|oyez|listen|attention)"), Text) || Exclamations >= 2))
    {
        return Signature;
    }

    return Roll < 0.65f ? TEXT("Interact") : FString();
}

TArray<uint8> UNpcSceneComponent::EncodeSpeechWav(const TArray<float>& Interleaved, int32 NumChannels, int32 SampleRate)
{
    NumChannels = FMath::Max(1, NumChannels);
    const int32 Frames = Interleaved.Num() / NumChannels;
    TArray<float> Mono;
    Mono.SetNumUninitialized(Frames);
    for (int32 f = 0; f < Frames; ++f)
    {
        float Sum = 0;
        for (int32 c = 0; c < NumChannels; ++c)
        {
            Sum += Interleaved[f * NumChannels + c];
        }

        Mono[f] = Sum / NumChannels;
    }

    // Linear-interpolation resample to 16 kHz: plenty for speech recognition.
    const int64 OutFrames = SampleRate > 0 ? static_cast<int64>(Frames) * SpeechSampleRate / SampleRate : 0;
    const double Step = static_cast<double>(SampleRate) / SpeechSampleRate;
    TArray<uint8> Wav;
    Wav.Reserve(44 + OutFrames * 2);
    WriteTag(Wav, "RIFF");
    WriteInt32(Wav, 36 + static_cast<int32>(OutFrames) * 2);
    WriteTag(Wav, "WAVE");
    WriteTag(Wav, "fmt ");
    WriteInt32(Wav, 16);
    WriteInt16(Wav, 1);
    WriteInt16(Wav, 1);
    WriteInt32(Wav, SpeechSampleRate);
    WriteInt32(Wav, SpeechSampleRate * 2);
    WriteInt16(Wav, 2);
    WriteInt16(Wav, 16);
    WriteTag(Wav, "data");
    WriteInt32(Wav, static_cast<int32>(OutFrames) * 2);
    for (int64 i = 0; i < OutFrames; ++i)
    {
        const double Pos = i * Step;
        const int32 Index = static_cast<int32>(Pos);
        const float Frac = static_cast<float>(Pos - Index);
        const float A = Mono[FMath::Min(Index, Frames - 1)];
        const float B = Mono[FMath::Min(Index + 1, Frames - 1)];
        const float S = FMath::Clamp(A + (B - A) * Frac, -1.f, 1.f);
        WriteInt16(Wav, static_cast<int16>(S < 0 ? S * 32768.f : S * 32767.f));
    }

    return Wav;
}

bool UNpcSceneComponent::DecodePcm16Wav(const TArray<uint8>& Wav, TArray<int16>& OutSamples, int32& OutChannels, int32& OutSampleRate)
{
    if (Wav.Num() < 44 || FMemory::Memcmp(Wav.GetData(), "RIFF", 4) != 0 || FMemory::Memcmp(Wav.GetData() + 8, "WAVE", 4) != 0)
    {
        return false;
    }

    int32 Bits = 0;
    int32 Offset = 12;
    while (Offset + 8 <= Wav.Num())
    {
        int32 Size = 0;
        FMemory::Memcpy(&Size, Wav.GetData() + Offset + 4, 4);
        const int32 Body = Offset + 8;
        if (FMemory::Memcmp(Wav.GetData() + Offset, "fmt ", 4) == 0 && Body + 16 <= Wav.Num())
        {
            int16 Channels = 0;
            int16 BitsPerSample = 0;
            FMemory::Memcpy(&Channels, Wav.GetData() + Body + 2, 2);
            FMemory::Memcpy(&OutSampleRate, Wav.GetData() + Body + 4, 4);
            FMemory::Memcpy(&BitsPerSample, Wav.GetData() + Body + 14, 2);
            OutChannels = Channels;
            Bits = BitsPerSample;
        }
        else if (FMemory::Memcmp(Wav.GetData() + Offset, "data", 4) == 0)
        {
            if (Bits != 16 || OutChannels <= 0 || OutSampleRate <= 0)
            {
                return false;
            }

            const int32 Count = FMath::Min(Size, Wav.Num() - Body) / 2;
            OutSamples.SetNumUninitialized(Count);
            FMemory::Memcpy(OutSamples.GetData(), Wav.GetData() + Body, Count * 2);
            return true;
        }

        Offset = Body + Size + (Size & 1);
    }

    return false;
}
