# Documentation Updates - November 29, 2025

## Overview

This directory contains comprehensive documentation for the GameRagKit bug fix and improvements released on November 29, 2025.

## What's in This Directory

### [PROVIDER_COMPATIBILITY.md](PROVIDER_COMPATIBILITY.md)
**Cloud Provider Compatibility Guide**
- Complete list of supported providers (OpenAI, Azure, Gemini, Groq, OpenRouter, Mistral)
- Which providers are NOT supported (Anthropic/Claude, Cohere) and why
- Implementation details and API format differences
- Model names, endpoints, and environment variables for each provider
- Testing instructions and FAQ

### [CHANGELOG_2025-11-29.md](CHANGELOG_2025-11-29.md)
**Quick Summary of Changes**
- What was fixed (critical YAML deserialization bug)
- What was added (documentation, examples, docker-compose)
- Testing results
- Version recommendations

### [ISSUES_AND_IMPROVEMENTS.md](ISSUES_AND_IMPROVEMENTS.md)
**Detailed Analysis** (for maintainers and contributors)
- Complete reproduction steps
- Root cause analysis of the bug
- All 7 issues found (1 critical, 6 documentation gaps)
- Concrete recommendations for future improvements
- Testing checklist for releases
- Provider compatibility matrix

### [QUICK_ISSUE_SUMMARY.md](QUICK_ISSUE_SUMMARY.md)
**Executive Summary** (TL;DR version)
- The bug in 3 sentences
- Missing documentation list
- Quick action items for maintainers

### [READY_TO_COMMIT.md](READY_TO_COMMIT.md)
**Commit Guide**
- List of all files changed
- Suggested commit message
- Step-by-step commit instructions
- Testing verification steps

## Critical Bug Fixed

**Problem:** Library completely unusable - couldn't load ANY configuration files due to YamlDotNet failing to deserialize C# `record` types without parameterless constructors.

**Solution:** Changed `NpcConfig` from record to class with init properties.

**Impact:** Library now works correctly, all 16 unit tests pass.

## Documentation Added

1. **docker-compose.yml** - One-command database setup
2. **Enhanced .env.example** - All providers documented with 2025 model names
3. **Example configurations** in `examples/configs/`:
   - Gemini (using latest Gemini 2.0/2.5 models)
   - OpenAI (using latest GPT-4.1 models)
   - Ollama (fully offline)
   - Hybrid (smart routing)
4. **Updated README** - Clear folder structure and "Where to Find What" guide

## Latest Model Names (2025)

### Google Gemini
- Chat: `gemini-2.0-flash-exp`, `gemini-2.5-flash`, `gemini-2.5-pro`
- Embeddings: `text-embedding-004`
- API Key: https://aistudio.google.com/apikey

### OpenAI
- Chat: `gpt-4.1`, `gpt-4.1-mini`, `gpt-4o`
- Embeddings: `text-embedding-3-large` (3072 dims), `text-embedding-3-small` (1536 dims)
- API Key: https://platform.openai.com/api-keys

### Ollama (Local)
- Chat: `llama3.2:3b-instruct-q4_K_M`
- Embeddings: `nomic-embed-text`
- No API key needed

## Quick Links

- **Main README**: [../../README.md](../../README.md)
- **Example Configs**: [../../examples/configs/](../../examples/configs/)
- **Docker Setup**: [../../docker-compose.yml](../../docker-compose.yml)
- **Environment Template**: [../../.env.example](../../.env.example)

## For Maintainers

All changes are ready to commit and push. The CI/CD pipeline will automatically build and publish the updated NuGet package.

See [READY_TO_COMMIT.md](READY_TO_COMMIT.md) for exact commit steps.

## Sources

Model information sourced from official documentation:
- [Gemini Models Documentation](https://ai.google.dev/gemini-api/docs/models)
- [OpenAI Model Release Notes](https://help.openai.com/en/articles/9624314-model-release-notes)
- [OpenAI Text Embedding Models](https://platform.openai.com/docs/models/text-embedding-3-large)
- [Gemini 2.5 Flash Updates](https://developers.googleblog.com/en/continuing-to-bring-you-our-latest-models-with-an-improved-gemini-2-5-flash-and-flash-lite-release/)
- [GPT-4.1 Launch](https://openai.com/index/gpt-4-1/)

---

**Date:** November 29, 2025
**Status:** Ready for release
**Test Status:** ✅ All 16 tests pass
