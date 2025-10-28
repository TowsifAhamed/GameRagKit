#include "AskNpcLibrary.h"
#include "HttpModule.h"
#include "Interfaces/IHttpRequest.h"
#include "Interfaces/IHttpResponse.h"
#include "Dom/JsonObject.h"
#include "Serialization/JsonSerializer.h"

void UAskNpcLibrary::AskNpc(const FString& NpcId, const FString& Question, FAskNpcResponse& Response)
{
    const TSharedRef<IHttpRequest, ESPMode::ThreadSafe> Request = FHttpModule::Get().CreateRequest();
    Request->SetURL(TEXT("http://127.0.0.1:5280/ask"));
    Request->SetVerb(TEXT("POST"));
    Request->SetHeader(TEXT("Content-Type"), TEXT("application/json"));

    FString Payload;
    const TSharedPtr<FJsonObject> Body = MakeShared<FJsonObject>();
    Body->SetStringField(TEXT("npc"), NpcId);
    Body->SetStringField(TEXT("question"), Question);

    const TSharedRef<TJsonWriter<>> Writer = TJsonWriterFactory<>::Create(&Payload);
    FJsonSerializer::Serialize(Body.ToSharedRef(), Writer);

    Request->SetContentAsString(Payload);

    const FHttpResponsePtr HttpResponse = FHttpModule::Get().GetHttpManager().CreateRequestThreadSafe(Request)->ProcessRequest();
    if (HttpResponse.IsValid() && HttpResponse->GetResponseCode() == 200)
    {
        TSharedPtr<FJsonObject> Json;
        const TSharedRef<TJsonReader<>> Reader = TJsonReaderFactory<>::Create(HttpResponse->GetContentAsString());
        if (FJsonSerializer::Deserialize(Reader, Json) && Json.IsValid())
        {
            Response.Answer = Json->GetStringField(TEXT("answer"));

            const TArray<TSharedPtr<FJsonValue>>* SourceArray;
            if (Json->TryGetArrayField(TEXT("sources"), SourceArray))
            {
                for (const TSharedPtr<FJsonValue>& Value : *SourceArray)
                {
                    Response.Sources.Add(Value->AsString());
                }
            }

            const TArray<TSharedPtr<FJsonValue>>* ScoreArray;
            if (Json->TryGetArrayField(TEXT("scores"), ScoreArray))
            {
                for (const TSharedPtr<FJsonValue>& Value : *ScoreArray)
                {
                    Response.Scores.Add(static_cast<float>(Value->AsNumber()));
                }
            }

            Response.bFromCloud = Json->GetBoolField(TEXT("fromCloud"));
        }
    }
}
