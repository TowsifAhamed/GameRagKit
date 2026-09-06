#pragma once

#include "CoreMinimal.h"
#include "Components/ActorComponent.h"
#include "Interfaces/IHttpRequest.h"
#include "Dom/JsonValue.h"
#include "NpcDialogueComponent.generated.h"

USTRUCT(BlueprintType)
struct FNpcAction
{
    GENERATED_BODY()

    UPROPERTY(BlueprintReadOnly, Category = "GameRagKit")
    FString Name;

    UPROPERTY(BlueprintReadOnly, Category = "GameRagKit")
    TMap<FString, FString> Args;
};

USTRUCT(BlueprintType)
struct FNpcResponse
{
    GENERATED_BODY()

    UPROPERTY(BlueprintReadOnly, Category = "GameRagKit")
    FString Answer;

    UPROPERTY(BlueprintReadOnly, Category = "GameRagKit")
    TArray<FString> Sources;

    UPROPERTY(BlueprintReadOnly, Category = "GameRagKit")
    TArray<float> Scores;

    UPROPERTY(BlueprintReadOnly, Category = "GameRagKit")
    bool bFromCloud = false;

    UPROPERTY(BlueprintReadOnly, Category = "GameRagKit")
    TArray<FNpcAction> Actions;
};

USTRUCT(BlueprintType)
struct FNpcNearbyEntity
{
    GENERATED_BODY()

    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "GameRagKit")
    FString Id;

    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "GameRagKit")
    FString Type;

    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "GameRagKit")
    float DistanceMeters = 0.f;
};

USTRUCT(BlueprintType)
struct FNpcInventoryItem
{
    GENERATED_BODY()

    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "GameRagKit")
    FString ItemId;

    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "GameRagKit")
    int32 Quantity = 1;
};

USTRUCT(BlueprintType)
struct FNpcWorldState
{
    GENERATED_BODY()

    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "GameRagKit")
    FString TimeOfDay;

    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "GameRagKit")
    bool bInCombat = false;

    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "GameRagKit")
    TArray<FNpcNearbyEntity> NearbyEntities;

    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "GameRagKit")
    TArray<FNpcInventoryItem> PlayerInventory;
};

DECLARE_DYNAMIC_MULTICAST_DELEGATE_OneParam(FOnNpcResponseReceived, const FNpcResponse&, Response);
DECLARE_DYNAMIC_MULTICAST_DELEGATE_OneParam(FOnNpcTextChunk, const FString&, TextChunk);
DECLARE_DYNAMIC_MULTICAST_DELEGATE_TwoParams(FOnNpcStreamComplete, const TArray<FString>&, Sources, const TArray<FNpcAction>&, Actions);
DECLARE_DYNAMIC_MULTICAST_DELEGATE_OneParam(FOnNpcError, const FString&, ErrorMessage);
DECLARE_DYNAMIC_MULTICAST_DELEGATE_OneParam(FOnNpcHealthChecked, bool, bHealthy);

/**
 * HTTP client component for a GameRagKit server. This plugin does not embed the
 * GameRagKit RAG/LLM runtime -- run a GameRagKit server separately (`gamerag serve`)
 * and point this component at it.
 */
UCLASS(ClassGroup = (GameRagKit), meta = (BlueprintSpawnableComponent))
class GAMERAGKIT_API UNpcDialogueComponent : public UActorComponent
{
    GENERATED_BODY()

public:
    UNpcDialogueComponent();

    /** URL of the GameRagKit server (e.g., http://localhost:5280) */
    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "GameRagKit|Configuration")
    FString ServerUrl = TEXT("http://localhost:5280");

    /** API key for authentication (if the server requires one) */
    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "GameRagKit|Configuration")
    FString ApiKey;

    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "GameRagKit|Configuration")
    bool bEnableLogging = true;

    UPROPERTY(BlueprintAssignable, Category = "GameRagKit|Events")
    FOnNpcResponseReceived OnResponseReceived;

    UPROPERTY(BlueprintAssignable, Category = "GameRagKit|Events")
    FOnNpcTextChunk OnTextChunkReceived;

    UPROPERTY(BlueprintAssignable, Category = "GameRagKit|Events")
    FOnNpcStreamComplete OnStreamComplete;

    UPROPERTY(BlueprintAssignable, Category = "GameRagKit|Events")
    FOnNpcError OnError;

    UPROPERTY(BlueprintAssignable, Category = "GameRagKit|Events")
    FOnNpcHealthChecked OnHealthChecked;

    /** Ask a question to an NPC (non-streaming). Result arrives via OnResponseReceived/OnError. */
    UFUNCTION(BlueprintCallable, Category = "GameRagKit")
    void AskNpc(const FString& NpcId, const FString& Question, float Importance = 0.3f, const FNpcWorldState& WorldState = FNpcWorldState());

    /**
     * Ask a question with a genuinely incremental streaming response, suitable for a
     * typewriter effect: OnTextChunkReceived fires as each chunk arrives over the wire,
     * not once at the end. Result arrives via OnStreamComplete/OnError.
     */
    UFUNCTION(BlueprintCallable, Category = "GameRagKit")
    void AskNpcStreaming(const FString& NpcId, const FString& Question, float Importance = 0.3f, const FNpcWorldState& WorldState = FNpcWorldState());

    /** Check if the GameRagKit server is healthy. Result arrives via OnHealthChecked. */
    UFUNCTION(BlueprintCallable, Category = "GameRagKit")
    void CheckServerHealth();

private:
    void ApplyCommonHeaders(TSharedRef<IHttpRequest> Request) const;
    FString BuildRequestBody(const FString& NpcId, const FString& Question, float Importance, const FNpcWorldState& WorldState) const;

    void OnAskResponseReceived(FHttpRequestPtr Request, FHttpResponsePtr Response, bool bWasSuccessful);
    void OnStreamResponseReceived(FHttpRequestPtr Request, FHttpResponsePtr Response, bool bWasSuccessful);
    void OnHealthCheckReceived(FHttpRequestPtr Request, FHttpResponsePtr Response, bool bWasSuccessful);

    static bool ParseNpcResponse(const FString& JsonString, FNpcResponse& OutResponse);
    static bool ParseActionsArray(const TArray<TSharedPtr<FJsonValue>>& ActionsArray, TArray<FNpcAction>& OutActions);

    /** Called incrementally as SSE bytes arrive; splits on newlines and processes complete "data: ..." lines. */
    void HandleStreamedBytes(void* Ptr, int64& Length);
    void ProcessSseLine(const FString& Line);

    FString StreamLineBuffer;
    TArray<FString> StreamSources;
    TArray<FNpcAction> StreamActions;
};
