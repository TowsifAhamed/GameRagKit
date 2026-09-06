#include "NpcDialogueComponent.h"
#include "HttpModule.h"
#include "Interfaces/IHttpResponse.h"
#include "Dom/JsonObject.h"
#include "Dom/JsonValue.h"
#include "Serialization/JsonSerializer.h"

namespace
{
    constexpr const TCHAR* ProtocolVersion = TEXT("1");
    constexpr const TCHAR* DataPrefix = TEXT("data:");
}

UNpcDialogueComponent::UNpcDialogueComponent()
{
    PrimaryComponentTick.bCanEverTick = false;
}

void UNpcDialogueComponent::ApplyCommonHeaders(TSharedRef<IHttpRequest> Request) const
{
    Request->SetHeader(TEXT("Content-Type"), TEXT("application/json"));
    Request->SetHeader(TEXT("X-GameRAG-Protocol"), ProtocolVersion);
    if (!ApiKey.IsEmpty())
    {
        Request->SetHeader(TEXT("X-Api-Key"), ApiKey);
    }
}

FString UNpcDialogueComponent::BuildRequestBody(const FString& NpcId, const FString& Question, float Importance, const FNpcWorldState& WorldState) const
{
    TSharedPtr<FJsonObject> Root = MakeShared<FJsonObject>();
    Root->SetStringField(TEXT("npc"), NpcId);
    Root->SetStringField(TEXT("question"), Question);

    TSharedPtr<FJsonObject> Options = MakeShared<FJsonObject>();
    Options->SetNumberField(TEXT("importance"), Importance);

    const bool bHasWorldState = !WorldState.TimeOfDay.IsEmpty()
        || WorldState.bInCombat
        || WorldState.NearbyEntities.Num() > 0
        || WorldState.PlayerInventory.Num() > 0;

    if (bHasWorldState)
    {
        TSharedPtr<FJsonObject> WorldStateJson = MakeShared<FJsonObject>();
        if (!WorldState.TimeOfDay.IsEmpty())
        {
            WorldStateJson->SetStringField(TEXT("timeOfDay"), WorldState.TimeOfDay);
        }

        WorldStateJson->SetBoolField(TEXT("inCombat"), WorldState.bInCombat);

        TArray<TSharedPtr<FJsonValue>> NearbyJson;
        for (const FNpcNearbyEntity& Entity : WorldState.NearbyEntities)
        {
            TSharedPtr<FJsonObject> EntityJson = MakeShared<FJsonObject>();
            EntityJson->SetStringField(TEXT("id"), Entity.Id);
            if (!Entity.Type.IsEmpty())
            {
                EntityJson->SetStringField(TEXT("type"), Entity.Type);
            }
            EntityJson->SetNumberField(TEXT("distanceMeters"), Entity.DistanceMeters);
            NearbyJson.Add(MakeShared<FJsonValueObject>(EntityJson));
        }
        WorldStateJson->SetArrayField(TEXT("nearbyEntities"), NearbyJson);

        TArray<TSharedPtr<FJsonValue>> InventoryJson;
        for (const FNpcInventoryItem& Item : WorldState.PlayerInventory)
        {
            TSharedPtr<FJsonObject> ItemJson = MakeShared<FJsonObject>();
            ItemJson->SetStringField(TEXT("itemId"), Item.ItemId);
            ItemJson->SetNumberField(TEXT("quantity"), Item.Quantity);
            InventoryJson.Add(MakeShared<FJsonValueObject>(ItemJson));
        }
        WorldStateJson->SetArrayField(TEXT("playerInventory"), InventoryJson);

        Options->SetObjectField(TEXT("worldState"), WorldStateJson);
    }

    Root->SetObjectField(TEXT("options"), Options);

    FString JsonString;
    TSharedRef<TJsonWriter<>> Writer = TJsonWriterFactory<>::Create(&JsonString);
    FJsonSerializer::Serialize(Root.ToSharedRef(), Writer);
    return JsonString;
}

void UNpcDialogueComponent::AskNpc(const FString& NpcId, const FString& Question, float Importance, const FNpcWorldState& WorldState)
{
    TSharedRef<IHttpRequest> Request = FHttpModule::Get().CreateRequest();
    Request->SetVerb(TEXT("POST"));
    Request->SetURL(ServerUrl + TEXT("/ask"));
    ApplyCommonHeaders(Request);
    Request->SetContentAsString(BuildRequestBody(NpcId, Question, Importance, WorldState));
    Request->OnProcessRequestComplete().BindUObject(this, &UNpcDialogueComponent::OnAskResponseReceived);
    Request->ProcessRequest();

    if (bEnableLogging)
    {
        UE_LOG(LogTemp, Log, TEXT("[GameRagKit] Asking %s: %s"), *NpcId, *Question);
    }
}

void UNpcDialogueComponent::AskNpcStreaming(const FString& NpcId, const FString& Question, float Importance, const FNpcWorldState& WorldState)
{
    StreamLineBuffer.Empty();
    StreamSources.Empty();
    StreamActions.Empty();

    TSharedRef<IHttpRequest> Request = FHttpModule::Get().CreateRequest();
    Request->SetVerb(TEXT("POST"));
    Request->SetURL(ServerUrl + TEXT("/ask/stream"));
    ApplyCommonHeaders(Request);
    Request->SetContentAsString(BuildRequestBody(NpcId, Question, Importance, WorldState));

    Request->SetResponseBodyReceiveStreamDelegateV2(FHttpRequestStreamDelegateV2::CreateUObject(
        this, &UNpcDialogueComponent::HandleStreamedBytes));

    Request->OnProcessRequestComplete().BindUObject(this, &UNpcDialogueComponent::OnStreamResponseReceived);
    Request->ProcessRequest();

    if (bEnableLogging)
    {
        UE_LOG(LogTemp, Log, TEXT("[GameRagKit] Asking %s (streaming): %s"), *NpcId, *Question);
    }
}

void UNpcDialogueComponent::CheckServerHealth()
{
    TSharedRef<IHttpRequest> Request = FHttpModule::Get().CreateRequest();
    Request->SetVerb(TEXT("GET"));
    Request->SetURL(ServerUrl + TEXT("/health"));
    Request->OnProcessRequestComplete().BindUObject(this, &UNpcDialogueComponent::OnHealthCheckReceived);
    Request->ProcessRequest();
}

void UNpcDialogueComponent::HandleStreamedBytes(void* Ptr, int64& Length)
{
    if (Ptr == nullptr || Length <= 0)
    {
        return;
    }

    const FUTF8ToTCHAR Converter(static_cast<const ANSICHAR*>(Ptr), static_cast<int32>(Length));
    StreamLineBuffer.AppendChars(Converter.Get(), Converter.Length());

    int32 NewlineIndex;
    while (StreamLineBuffer.FindChar(TEXT('\n'), NewlineIndex))
    {
        FString Line = StreamLineBuffer.Left(NewlineIndex);
        StreamLineBuffer.RemoveAt(0, NewlineIndex + 1);
        Line.TrimEndInline();
        ProcessSseLine(Line);
    }
}

void UNpcDialogueComponent::ProcessSseLine(const FString& Line)
{
    if (Line.IsEmpty() || !Line.StartsWith(DataPrefix))
    {
        return;
    }

    const FString Payload = Line.RightChop(FCString::Strlen(DataPrefix)).TrimStartAndEnd();
    if (Payload.IsEmpty())
    {
        return;
    }

    TSharedPtr<FJsonObject> JsonObject;
    const TSharedRef<TJsonReader<>> Reader = TJsonReaderFactory<>::Create(Payload);
    if (!FJsonSerializer::Deserialize(Reader, JsonObject) || !JsonObject.IsValid())
    {
        return;
    }

    FString EventType;
    JsonObject->TryGetStringField(TEXT("type"), EventType);

    if (EventType == TEXT("chunk"))
    {
        FString Text;
        if (JsonObject->TryGetStringField(TEXT("text"), Text) && !Text.IsEmpty())
        {
            OnTextChunkReceived.Broadcast(Text);
        }
    }
    else if (EventType == TEXT("end"))
    {
        const TArray<TSharedPtr<FJsonValue>>* SourcesArray;
        if (JsonObject->TryGetArrayField(TEXT("sources"), SourcesArray))
        {
            for (const TSharedPtr<FJsonValue>& Value : *SourcesArray)
            {
                StreamSources.Add(Value->AsString());
            }
        }

        const TArray<TSharedPtr<FJsonValue>>* ActionsArray;
        if (JsonObject->TryGetArrayField(TEXT("actions"), ActionsArray))
        {
            ParseActionsArray(*ActionsArray, StreamActions);
        }
    }
}

void UNpcDialogueComponent::OnAskResponseReceived(FHttpRequestPtr Request, FHttpResponsePtr Response, bool bWasSuccessful)
{
    if (!bWasSuccessful || !Response.IsValid())
    {
        const FString Error = TEXT("Request failed: Connection error");
        UE_LOG(LogTemp, Error, TEXT("[GameRagKit] %s"), *Error);
        OnError.Broadcast(Error);
        return;
    }

    if (Response->GetResponseCode() != 200)
    {
        const FString Error = FString::Printf(TEXT("Request failed: HTTP %d"), Response->GetResponseCode());
        UE_LOG(LogTemp, Error, TEXT("[GameRagKit] %s"), *Error);
        OnError.Broadcast(Error);
        return;
    }

    FNpcResponse NpcResponse;
    if (!ParseNpcResponse(Response->GetContentAsString(), NpcResponse))
    {
        const FString Error = TEXT("Failed to parse response JSON");
        UE_LOG(LogTemp, Error, TEXT("[GameRagKit] %s"), *Error);
        OnError.Broadcast(Error);
        return;
    }

    if (bEnableLogging)
    {
        UE_LOG(LogTemp, Log, TEXT("[GameRagKit] Response: %s"), *NpcResponse.Answer);
    }

    OnResponseReceived.Broadcast(NpcResponse);
}

void UNpcDialogueComponent::OnStreamResponseReceived(FHttpRequestPtr Request, FHttpResponsePtr Response, bool bWasSuccessful)
{
    // Flush any trailing line that wasn't newline-terminated.
    if (!StreamLineBuffer.IsEmpty())
    {
        ProcessSseLine(StreamLineBuffer);
        StreamLineBuffer.Empty();
    }

    if (!bWasSuccessful || !Response.IsValid())
    {
        OnError.Broadcast(TEXT("Streaming request failed"));
        return;
    }

    if (bEnableLogging)
    {
        UE_LOG(LogTemp, Log, TEXT("[GameRagKit] Streaming complete"));
    }

    OnStreamComplete.Broadcast(StreamSources, StreamActions);
}

void UNpcDialogueComponent::OnHealthCheckReceived(FHttpRequestPtr Request, FHttpResponsePtr Response, bool bWasSuccessful)
{
    const bool bHealthy = bWasSuccessful && Response.IsValid() && Response->GetResponseCode() == 200;

    if (bEnableLogging)
    {
        UE_LOG(LogTemp, Log, TEXT("[GameRagKit] Server health: %s"), bHealthy ? TEXT("OK") : TEXT("FAILED"));
    }

    OnHealthChecked.Broadcast(bHealthy);
}

bool UNpcDialogueComponent::ParseNpcResponse(const FString& JsonString, FNpcResponse& OutResponse)
{
    TSharedPtr<FJsonObject> JsonObject;
    const TSharedRef<TJsonReader<>> Reader = TJsonReaderFactory<>::Create(JsonString);
    if (!FJsonSerializer::Deserialize(Reader, JsonObject) || !JsonObject.IsValid())
    {
        return false;
    }

    JsonObject->TryGetStringField(TEXT("answer"), OutResponse.Answer);
    JsonObject->TryGetBoolField(TEXT("fromCloud"), OutResponse.bFromCloud);

    const TArray<TSharedPtr<FJsonValue>>* SourcesArray;
    if (JsonObject->TryGetArrayField(TEXT("sources"), SourcesArray))
    {
        for (const TSharedPtr<FJsonValue>& Value : *SourcesArray)
        {
            OutResponse.Sources.Add(Value->AsString());
        }
    }

    const TArray<TSharedPtr<FJsonValue>>* ScoresArray;
    if (JsonObject->TryGetArrayField(TEXT("scores"), ScoresArray))
    {
        for (const TSharedPtr<FJsonValue>& Value : *ScoresArray)
        {
            OutResponse.Scores.Add(static_cast<float>(Value->AsNumber()));
        }
    }

    const TArray<TSharedPtr<FJsonValue>>* ActionsArray;
    if (JsonObject->TryGetArrayField(TEXT("actions"), ActionsArray))
    {
        ParseActionsArray(*ActionsArray, OutResponse.Actions);
    }

    return true;
}

bool UNpcDialogueComponent::ParseActionsArray(const TArray<TSharedPtr<FJsonValue>>& ActionsArray, TArray<FNpcAction>& OutActions)
{
    for (const TSharedPtr<FJsonValue>& ActionValue : ActionsArray)
    {
        const TSharedPtr<FJsonObject>* ActionObject;
        if (!ActionValue->TryGetObject(ActionObject))
        {
            continue;
        }

        FNpcAction Action;
        (*ActionObject)->TryGetStringField(TEXT("name"), Action.Name);

        const TSharedPtr<FJsonObject>* ArgsObject;
        if ((*ActionObject)->TryGetObjectField(TEXT("args"), ArgsObject))
        {
            for (const auto& Pair : (*ArgsObject)->Values)
            {
                Action.Args.Add(FString(Pair.Key), Pair.Value->AsString());
            }
        }

        OutActions.Add(Action);
    }

    return true;
}
