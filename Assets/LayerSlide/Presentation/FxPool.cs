using LayerSlide.Core;
using UnityEngine;

namespace LayerSlide.Presentation
{
    /// <summary>
    /// Fixed pool of 64 particles (GDD 04 §12, extended for the toy look). Bursts are small chunky cubes that pop
    /// outward and fall back; confetti are thin tumbling paper strips with gravity and a longer life. Spread and
    /// spin derive from a seed, so effects are deterministic; when full, the oldest particle is reused.
    /// </summary>
    public sealed class FxPool : MonoBehaviour
    {
        const int Capacity = 64;
        const float BurstLifetime = 0.5f;
        const float ConfettiLifetime = 1.6f;
        const float Gravity = 7f;

        struct Particle
        {
            public Transform Transform;
            public MeshFilter Filter;
            public MeshRenderer Renderer;
            public Vector3 Velocity;
            public Vector3 Spin;
            public float Age;
            public float Lifetime;
            public float Drag;
            public int SpawnOrder;
        }

        readonly Particle[] _particles = new Particle[Capacity];
        VisualTheme _theme;
        Mesh _cube, _strip;
        int _spawnCounter;

        public void Initialize(VisualTheme theme)
        {
            _theme = theme;
            _cube = MeshLibrary.Box(0.07f, 0.07f, 0.07f);
            _strip = MeshLibrary.Box(0.12f, 0.012f, 0.07f);
            for (int i = 0; i < Capacity; i++)
            {
                var go = BlockView.CreateRenderer("Particle" + i, transform, _cube, theme.white, castShadows: false);
                go.SetActive(false);
                _particles[i] = new Particle
                {
                    Transform = go.transform,
                    Filter = go.GetComponent<MeshFilter>(),
                    Renderer = go.GetComponent<MeshRenderer>(),
                    Age = 1f,
                    Lifetime = 1f
                };
            }
        }

        /// <summary>Burst of <paramref name="count"/> chunky bits in one color (last layer cleared).</summary>
        public void Burst(Vector3 origin, LayerColor color, int count, int seed)
        {
            for (int i = 0; i < count; i++)
            {
                var direction = Direction(seed, i, count, 1.1f);
                Spawn(origin, _cube, _theme.GetLayerMaterial(color), direction * 2.6f, SpinOf(seed, i, 360f), BurstLifetime, 0.5f);
            }
            // A few white sparkles on top.
            for (int i = 0; i < count / 2; i++)
                Spawn(origin + Vector3.up * 0.1f, _cube, _theme.white, Direction(seed + 7, i, count / 2, 1.8f) * 2f, SpinOf(seed, i + 9, 540f),
                    BurstLifetime * 0.8f, 0.5f);
        }

        /// <summary>Win confetti in all three colors plus white, thrown up and drifting down.</summary>
        public void Confetti(Vector3 origin, int count, int seed)
        {
            for (int i = 0; i < count; i++)
            {
                var direction = Direction(seed, i, count, 2.6f);
                float speed = 4.2f + Hash(seed, i) * 2.2f;
                var material = i % 4 == 3 ? _theme.white : _theme.GetLayerMaterial((LayerColor)(i % 3));
                Spawn(origin, _strip, material, direction * speed, SpinOf(seed, i, 720f), ConfettiLifetime, 1.8f);
            }
        }

        public void Clear()
        {
            for (int i = 0; i < Capacity; i++)
            {
                _particles[i].Age = _particles[i].Lifetime;
                _particles[i].Transform.gameObject.SetActive(false);
            }
        }

        void Spawn(Vector3 origin, Mesh mesh, Material material, Vector3 velocity, Vector3 spin, float lifetime, float drag)
        {
            int slot = FindSlot();
            ref var particle = ref _particles[slot];
            particle.Age = 0f;
            particle.Lifetime = lifetime;
            particle.Velocity = velocity;
            particle.Spin = spin;
            particle.Drag = drag;
            particle.SpawnOrder = _spawnCounter++;
            particle.Filter.sharedMesh = mesh;
            particle.Renderer.sharedMaterial = material;
            particle.Transform.localPosition = origin;
            particle.Transform.localRotation = Quaternion.Euler(spin * 0.1f);
            particle.Transform.localScale = Vector3.one;
            particle.Transform.gameObject.SetActive(true);
        }

        int FindSlot()
        {
            int oldest = 0;
            for (int i = 0; i < Capacity; i++)
            {
                if (_particles[i].Age >= _particles[i].Lifetime) return i;
                if (_particles[i].SpawnOrder < _particles[oldest].SpawnOrder) oldest = i;
            }
            return oldest;
        }

        static float Hash(int seed, int index)
        {
            uint h = (uint)(seed * 73856093) ^ (uint)(index * 19349663);
            h ^= h >> 13;
            h *= 0x5bd1e995;
            h ^= h >> 15;
            return (h & 0xFFFF) / 65535f;
        }

        static Vector3 SpinOf(int seed, int index, float maxDegrees) =>
            new Vector3(Hash(seed, index * 3) - 0.5f, Hash(seed, index * 3 + 1) - 0.5f, Hash(seed, index * 3 + 2) - 0.5f) * (2f * maxDegrees);

        static Vector3 Direction(int seed, int index, int count, float up)
        {
            // Even angular spread with a small seeded offset; deterministic for a given command index.
            float offset = (seed * 0.618034f) % 1f;
            float angle = (index + offset) / count * Mathf.PI * 2f;
            float lift = up * (0.75f + 0.5f * Hash(seed, index + 101));
            return new Vector3(Mathf.Cos(angle), lift, Mathf.Sin(angle)).normalized;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            for (int i = 0; i < Capacity; i++)
            {
                ref var particle = ref _particles[i];
                if (particle.Age >= particle.Lifetime) continue;

                particle.Age += dt;
                if (particle.Age >= particle.Lifetime)
                {
                    particle.Transform.gameObject.SetActive(false);
                    continue;
                }

                particle.Velocity.y -= Gravity * dt;
                particle.Velocity *= 1f / (1f + particle.Drag * dt);
                particle.Transform.localPosition += particle.Velocity * dt;
                particle.Transform.localRotation *= Quaternion.Euler(particle.Spin * dt);

                // Full size for most of the life, then shrink away.
                float t = particle.Age / particle.Lifetime;
                particle.Transform.localScale = Vector3.one * Mathf.Clamp01((1f - t) * 4f);
            }
        }
    }
}
