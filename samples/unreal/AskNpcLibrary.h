#pragma once

#include "Kismet/BlueprintFunctionLibrary.h"
#include "AskNpcLibrary.generated.h"

USTRUCT(BlueprintType)
struct FAskNpcResponse
{
    GENERATED_BODY()

    UPROPERTY(BlueprintReadOnly)
    FString Answer;

    UPROPERTY(BlueprintReadOnly)
    TArray<FString> Sources;

    UPROPERTY(BlueprintReadOnly)
    TArray<float> Scores;

    UPROPERTY(BlueprintReadOnly)
    bool bFromCloud = false;
};

UCLASS()
class UAskNpcLibrary : public UBlueprintFunctionLibrary
{
    GENERATED_BODY()

public:
    UFUNCTION(BlueprintCallable, Category = "GameRAG")
    static void AskNpc(const FString& NpcId, const FString& Question, FAskNpcResponse& Response);
};
