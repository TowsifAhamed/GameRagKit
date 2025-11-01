# Unity Sample

The Unity demo will showcase `UnityWebRequest` with SSE support. For now, the key integration points are:

1. `POST /ask` for non-streaming answers.
2. `POST /ask/stream` for SSE. Each line begins with `data:` and ends with a blank line.
3. Include the `X-GameRAG-Protocol: 1` header.

The client repo (`gameragkit-java`) contains a reference SSE parser that mirrors what Unity needs.
