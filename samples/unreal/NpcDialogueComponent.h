// Copyright Epic Games, Inc. All Rights Reserved.
// GameRagKit Unreal Engine Integration

#pragma once

#include "CoreMinimal.h"
#include "Components/ActorComponent.h"
#include "Http.h"
#include "NpcDialogueComponent.generated.h"

/**
 * Response structure from GameRagKit /ask endpoint
 */
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
	bool FromCloud;

	UPROPERTY(BlueprintReadOnly, Category = "GameRagKit")
	int32 ResponseTimeMs;
};

/**
 * Delegate for successful NPC response
 */
DECLARE_DYNAMIC_MULTICAST_DELEGATE_OneParam(FOnNpcResponseReceived, const FNpcResponse&, Response);

/**
 * Delegate for streaming text chunks
 */
DECLARE_DYNAMIC_MULTICAST_DELEGATE_OneParam(FOnNpcTextChunk, const FString&, TextChunk);

/**
 * Delegate for errors
 */
DECLARE_DYNAMIC_MULTICAST_DELEGATE_OneParam(FOnNpcError, const FString&, ErrorMessage);

/**
 * Component for integrating GameRagKit NPCs into Unreal Engine.
 * Attach this to any Actor that needs to interact with NPCs.
 */
UCLASS(ClassGroup=(Custom), meta=(BlueprintSpawnableComponent))
class YOURGAME_API UNpcDialogueComponent : public UActorComponent
{
	GENERATED_BODY()

public:
	UNpcDialogueComponent();

	/**
	 * URL of the GameRagKit server (e.g., http://localhost:5280)
	 */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "GameRagKit|Configuration")
	FString ServerUrl = TEXT("http://localhost:5280");

	/**
	 * API key for authentication (if SERVER_API_KEY is set on server)
	 */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "GameRagKit|Configuration")
	FString ApiKey;

	/**
	 * Enable debug logging
	 */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "GameRagKit|Configuration")
	bool bEnableLogging = true;

	/**
	 * Called when NPC response is received
	 */
	UPROPERTY(BlueprintAssignable, Category = "GameRagKit|Events")
	FOnNpcResponseReceived OnResponseReceived;

	/**
	 * Called for each text chunk in streaming mode
	 */
	UPROPERTY(BlueprintAssignable, Category = "GameRagKit|Events")
	FOnNpcTextChunk OnTextChunkReceived;

	/**
	 * Called when an error occurs
	 */
	UPROPERTY(BlueprintAssignable, Category = "GameRagKit|Events")
	FOnNpcError OnError;

	/**
	 * Ask a question to an NPC (non-streaming)
	 *
	 * @param NpcId - The NPC identifier (e.g., "guard-north-gate")
	 * @param Question - The player's question
	 * @param Importance - Importance level 0.0-1.0 (affects local vs cloud routing)
	 */
	UFUNCTION(BlueprintCallable, Category = "GameRagKit")
	void AskNpc(const FString& NpcId, const FString& Question, float Importance = 0.3f);

	/**
	 * Ask a question to an NPC with streaming response (for typewriter effects)
	 *
	 * @param NpcId - The NPC identifier
	 * @param Question - The player's question
	 * @param Importance - Importance level 0.0-1.0
	 */
	UFUNCTION(BlueprintCallable, Category = "GameRagKit")
	void AskNpcStreaming(const FString& NpcId, const FString& Question, float Importance = 0.3f);

	/**
	 * Check if the GameRagKit server is healthy
	 */
	UFUNCTION(BlueprintCallable, Category = "GameRagKit")
	void CheckServerHealth();

protected:
	virtual void BeginPlay() override;

private:
	void OnAskResponseReceived(FHttpRequestPtr Request, FHttpResponsePtr Response, bool bWasSuccessful);
	void OnStreamResponseReceived(FHttpRequestPtr Request, FHttpResponsePtr Response, bool bWasSuccessful);
	void OnHealthCheckReceived(FHttpRequestPtr Request, FHttpResponsePtr Response, bool bWasSuccessful);

	void ParseNpcResponse(const FString& JsonString, FNpcResponse& OutResponse);
	void ParseStreamingResponse(const FString& ResponseBody);
};
