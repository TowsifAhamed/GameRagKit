// Copyright Epic Games, Inc. All Rights Reserved.
// GameRagKit Unreal Engine Integration

#include "NpcDialogueComponent.h"
#include "HttpModule.h"
#include "Interfaces/IHttpResponse.h"
#include "Json.h"
#include "JsonUtilities.h"

UNpcDialogueComponent::UNpcDialogueComponent()
{
	PrimaryComponentTick.bCanEverTick = false;
}

void UNpcDialogueComponent::BeginPlay()
{
	Super::BeginPlay();

	// Optionally check server health on start
	if (bEnableLogging)
	{
		UE_LOG(LogTemp, Log, TEXT("[GameRagKit] Component initialized. Server: %s"), *ServerUrl);
	}
}

void UNpcDialogueComponent::AskNpc(const FString& NpcId, const FString& Question, float Importance)
{
	// Create HTTP request
	TSharedRef<IHttpRequest, ESPMode::ThreadSafe> Request = FHttpModule::Get().CreateRequest();
	Request->SetVerb(TEXT("POST"));
	Request->SetURL(ServerUrl + TEXT("/ask"));
	Request->SetHeader(TEXT("Content-Type"), TEXT("application/json"));

	if (!ApiKey.IsEmpty())
	{
		Request->SetHeader(TEXT("X-API-Key"), ApiKey);
	}

	// Build JSON body
	TSharedPtr<FJsonObject> JsonObject = MakeShareable(new FJsonObject);
	JsonObject->SetStringField(TEXT("npc"), NpcId);
	JsonObject->SetStringField(TEXT("question"), Question);
	JsonObject->SetNumberField(TEXT("importance"), Importance);

	FString JsonString;
	TSharedRef<TJsonWriter<>> Writer = TJsonWriterFactory<>::Create(&JsonString);
	FJsonSerializer::Serialize(JsonObject.ToSharedRef(), Writer);

	Request->SetContentAsString(JsonString);

	// Bind response callback
	Request->OnProcessRequestComplete().BindUObject(this, &UNpcDialogueComponent::OnAskResponseReceived);

	// Send request
	Request->ProcessRequest();

	if (bEnableLogging)
	{
		UE_LOG(LogTemp, Log, TEXT("[GameRagKit] Asking %s: %s"), *NpcId, *Question);
	}
}

void UNpcDialogueComponent::AskNpcStreaming(const FString& NpcId, const FString& Question, float Importance)
{
	TSharedRef<IHttpRequest, ESPMode::ThreadSafe> Request = FHttpModule::Get().CreateRequest();
	Request->SetVerb(TEXT("POST"));
	Request->SetURL(ServerUrl + TEXT("/ask/stream"));
	Request->SetHeader(TEXT("Content-Type"), TEXT("application/json"));

	if (!ApiKey.IsEmpty())
	{
		Request->SetHeader(TEXT("X-API-Key"), ApiKey);
	}

	// Build JSON body
	TSharedPtr<FJsonObject> JsonObject = MakeShareable(new FJsonObject);
	JsonObject->SetStringField(TEXT("npc"), NpcId);
	JsonObject->SetStringField(TEXT("question"), Question);
	JsonObject->SetNumberField(TEXT("importance"), Importance);

	FString JsonString;
	TSharedRef<TJsonWriter<>> Writer = TJsonWriterFactory<>::Create(&JsonString);
	FJsonSerializer::Serialize(JsonObject.ToSharedRef(), Writer);

	Request->SetContentAsString(JsonString);
	Request->OnProcessRequestComplete().BindUObject(this, &UNpcDialogueComponent::OnStreamResponseReceived);
	Request->ProcessRequest();

	if (bEnableLogging)
	{
		UE_LOG(LogTemp, Log, TEXT("[GameRagKit] Asking %s (streaming): %s"), *NpcId, *Question);
	}
}

void UNpcDialogueComponent::CheckServerHealth()
{
	TSharedRef<IHttpRequest, ESPMode::ThreadSafe> Request = FHttpModule::Get().CreateRequest();
	Request->SetVerb(TEXT("GET"));
	Request->SetURL(ServerUrl + TEXT("/health"));
	Request->OnProcessRequestComplete().BindUObject(this, &UNpcDialogueComponent::OnHealthCheckReceived);
	Request->ProcessRequest();
}

void UNpcDialogueComponent::OnAskResponseReceived(FHttpRequestPtr Request, FHttpResponsePtr Response, bool bWasSuccessful)
{
	if (!bWasSuccessful || !Response.IsValid())
	{
		FString Error = TEXT("Request failed: Connection error");
		UE_LOG(LogTemp, Error, TEXT("[GameRagKit] %s"), *Error);
		OnError.Broadcast(Error);
		return;
	}

	if (Response->GetResponseCode() != 200)
	{
		FString Error = FString::Printf(TEXT("Request failed: HTTP %d"), Response->GetResponseCode());
		UE_LOG(LogTemp, Error, TEXT("[GameRagKit] %s"), *Error);
		OnError.Broadcast(Error);
		return;
	}

	// Parse response
	FNpcResponse NpcResponse;
	ParseNpcResponse(Response->GetContentAsString(), NpcResponse);

	if (bEnableLogging)
	{
		UE_LOG(LogTemp, Log, TEXT("[GameRagKit] Response: %s"), *NpcResponse.Answer);
		UE_LOG(LogTemp, Log, TEXT("[GameRagKit] From Cloud: %s, Time: %dms"),
			NpcResponse.FromCloud ? TEXT("Yes") : TEXT("No"),
			NpcResponse.ResponseTimeMs);
	}

	// Broadcast response
	OnResponseReceived.Broadcast(NpcResponse);
}

void UNpcDialogueComponent::OnStreamResponseReceived(FHttpRequestPtr Request, FHttpResponsePtr Response, bool bWasSuccessful)
{
	if (!bWasSuccessful || !Response.IsValid())
	{
		OnError.Broadcast(TEXT("Streaming request failed"));
		return;
	}

	ParseStreamingResponse(Response->GetContentAsString());
}

void UNpcDialogueComponent::OnHealthCheckReceived(FHttpRequestPtr Request, FHttpResponsePtr Response, bool bWasSuccessful)
{
	bool bHealthy = bWasSuccessful && Response.IsValid() && Response->GetResponseCode() == 200;

	if (bEnableLogging)
	{
		UE_LOG(LogTemp, Log, TEXT("[GameRagKit] Server health: %s"), bHealthy ? TEXT("OK") : TEXT("FAILED"));
	}
}

void UNpcDialogueComponent::ParseNpcResponse(const FString& JsonString, FNpcResponse& OutResponse)
{
	TSharedPtr<FJsonObject> JsonObject;
	TSharedRef<TJsonReader<>> Reader = TJsonReaderFactory<>::Create(JsonString);

	if (FJsonSerializer::Deserialize(Reader, JsonObject) && JsonObject.IsValid())
	{
		OutResponse.Answer = JsonObject->GetStringField(TEXT("answer"));
		OutResponse.FromCloud = JsonObject->GetBoolField(TEXT("fromCloud"));
		OutResponse.ResponseTimeMs = (int32)JsonObject->GetNumberField(TEXT("responseTimeMs"));

		// Parse sources array
		const TArray<TSharedPtr<FJsonValue>>* SourcesArray;
		if (JsonObject->TryGetArrayField(TEXT("sources"), SourcesArray))
		{
			for (const TSharedPtr<FJsonValue>& Value : *SourcesArray)
			{
				OutResponse.Sources.Add(Value->AsString());
			}
		}

		// Parse scores array
		const TArray<TSharedPtr<FJsonValue>>* ScoresArray;
		if (JsonObject->TryGetArrayField(TEXT("scores"), ScoresArray))
		{
			for (const TSharedPtr<FJsonValue>& Value : *ScoresArray)
			{
				OutResponse.Scores.Add((float)Value->AsNumber());
			}
		}
	}
}

void UNpcDialogueComponent::ParseStreamingResponse(const FString& ResponseBody)
{
	// Parse Server-Sent Events format (data: {...}\n\n)
	TArray<FString> Lines;
	ResponseBody.ParseIntoArrayLines(Lines);

	for (const FString& Line : Lines)
	{
		if (Line.StartsWith(TEXT("data:")))
		{
			FString JsonData = Line.RightChop(5).TrimStartAndEnd();
			if (!JsonData.IsEmpty())
			{
				TSharedPtr<FJsonObject> JsonObject;
				TSharedRef<TJsonReader<>> Reader = TJsonReaderFactory<>::Create(JsonData);

				if (FJsonSerializer::Deserialize(Reader, JsonObject) && JsonObject.IsValid())
				{
					FString EventType = JsonObject->GetStringField(TEXT("type"));

					if (EventType == TEXT("chunk"))
					{
						FString Text = JsonObject->GetStringField(TEXT("text"));
						OnTextChunkReceived.Broadcast(Text);
					}
					else if (EventType == TEXT("end"))
					{
						if (bEnableLogging)
						{
							UE_LOG(LogTemp, Log, TEXT("[GameRagKit] Streaming complete"));
						}
					}
				}
			}
		}
	}
}
