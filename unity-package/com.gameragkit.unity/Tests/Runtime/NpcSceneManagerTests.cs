using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.TestTools;

namespace GameRagKit.Unity.Tests
{
    /// <summary>
    /// End-to-end Play Mode tests against a running GameRagKit server with the tavern demo
    /// NPCs (scripts/run-voice-scene.sh). Server URL: GAMERAG_TEST_SERVER env var, default
    /// http://localhost:5290. Skipped (Ignored) when no server is reachable.
    /// </summary>
    public class NpcSceneManagerTests
    {
        private static string ServerUrl => Environment.GetEnvironmentVariable("GAMERAG_TEST_SERVER") ?? "http://localhost:5290";

        private NpcSceneManager _scene;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            using var health = UnityWebRequest.Get($"{ServerUrl}/health");
            yield return health.SendWebRequest();
            if (health.result != UnityWebRequest.Result.Success)
            {
                Assert.Ignore($"No GameRagKit server at {ServerUrl}");
            }

            var go = new GameObject("scene");
            _scene = go.AddComponent<NpcSceneManager>();
            _scene.serverUrl = ServerUrl;
            _scene.participants.Add(Participant("tavern-keeper-mira", "Mira"));
            _scene.participants.Add(Participant("blacksmith-bram", "Bram"));
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_scene != null)
            {
                foreach (var p in _scene.participants) UnityEngine.Object.Destroy(p.voice.gameObject);
                UnityEngine.Object.Destroy(_scene.gameObject);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator Say_Routes_To_The_Addressed_Npc_And_Plays_Its_Voice()
        {
            string method = null;
            NpcSceneLine first = null;
            AudioClip clip = null;
            _scene.Routed += (_, m) => method = m;
            _scene.LineStarted += line =>
            {
                first ??= line;
                clip ??= _scene.participants.First(p => p.npcId == line.NpcId).voice.clip;
            };

            _scene.Say("Bram, how is the forge today?");
            yield return WaitUntil(() => first != null, 60);

            Assert.AreEqual("addressed-by-name", method);
            Assert.AreEqual("blacksmith-bram", first.NpcId);
            Assert.IsNotEmpty(first.Text);
            Assert.IsNotNull(clip, "NPC voice clip should be assigned to its AudioSource");
            Assert.Greater(clip.length, 0.5f);
        }

        [UnityTest]
        public IEnumerator Interrupt_Keeps_Only_Heard_Words_And_Go_On_Resumes_The_Npc()
        {
            NpcSceneLine started = null;
            bool? interrupted = null;
            string said = null, unsaid = null, method = null;
            _scene.LineStarted += line => started ??= line;
            _scene.LineFinished += (line, wasInterrupted, s, u) =>
            {
                if (interrupted == null) { interrupted = wasInterrupted; said = s; unsaid = u; }
            };

            _scene.Say("Bram, tell me everything you know about the royal messenger.");
            yield return WaitUntil(() => started != null, 60);
            yield return new WaitForSeconds(2f);
            _scene.Interrupt();

            Assert.AreEqual(true, interrupted, "the playing line should finish as interrupted");
            Assert.IsNotEmpty(said);
            Assert.IsNotEmpty(unsaid);
            var last = _scene.History.Last(h => h.speaker != "player");
            Assert.IsTrue(last.interrupted);
            Assert.AreEqual(said, last.text, "history keeps only the heard words");

            _scene.Routed += (_, m) => method = m;
            _scene.Say("Sorry, go on.");
            yield return WaitUntil(() => method != null, 60);
            Assert.AreEqual("continue-interrupted", method);
        }

        private static SceneParticipant Participant(string id, string name)
        {
            var npc = new GameObject(name);
            return new SceneParticipant { npcId = id, displayName = name, voice = npc.AddComponent<AudioSource>(), root = npc.transform };
        }

        private static IEnumerator WaitUntil(Func<bool> condition, float timeoutSeconds)
        {
            var deadline = Time.realtimeSinceStartup + timeoutSeconds;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > deadline) Assert.Fail($"Timed out after {timeoutSeconds}s");
                yield return null;
            }
        }
    }
}
