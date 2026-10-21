using LayerSlide.Core;
using UnityEngine;

namespace LayerSlide.Presentation
{
    /// <summary>
    /// Fixed pool of 32 square particles (GDD 04 §12). No gravity, constant velocity, 400 ms life.
    /// Spread is derived from a seed, so effects are deterministic; when full, the oldest particle is reused.
    /// </summary>
    public sealed class FxPool : MonoBehaviour
    {
        const int Capacity = 32;
        const float Lifetime = 0.4f;
        const float Size = 0.04f;

        struct Particle
        {
            public Transform Transform;
            public MeshRenderer Renderer;
            public Vector3 Velocity;
            public float Age;
            public int SpawnOrder;
        }

        readonly Particle[] _particles = new Particle[Capacity];
        VisualTheme _theme;
        int _spawnCounter;

        public void Initialize(VisualTheme theme)
        {
            _theme = theme;
            var mesh = MeshLibrary.Box(Size, Size, Size);
            for (int i = 0; i < Capacity; i++)
            {
                var go = BlockView.CreateRenderer("Particle" + i, transform, mesh, theme.white, castShadows: false);
                go.SetActive(false);
                _particles[i] = new Particle { Transform = go.transform, Renderer = go.GetComponent<MeshRenderer>(), Age = Lifetime };
            }
        }

        /// <summary>Burst of <paramref name="count"/> particles in one color (last layer cleared).</summary>
        public void Burst(Vector3 origin, LayerColor color, int count, int seed)
        {
            for (int i = 0; i < count; i++)
                Spawn(origin, _theme.GetLayerMaterial(color), Direction(seed, i, count), 1.6f);
        }

        /// <summary>Win confetti in all three colors.</summary>
        public void Confetti(Vector3 origin, int count, int seed)
        {
            for (int i = 0; i < count; i++)
            {
                var direction = Direction(seed, i, count) + Vector3.up * 1.2f;
                Spawn(origin, _theme.GetLayerMaterial((LayerColor)(i % 3)), direction.normalized, 3.2f);
            }
        }

        public void Clear()
        {
            for (int i = 0; i < Capacity; i++)
            {
                _particles[i].Age = Lifetime;
                _particles[i].Transform.gameObject.SetActive(false);
            }
        }

        void Spawn(Vector3 origin, Material material, Vector3 direction, float speed)
        {
            int slot = FindSlot();
            ref var particle = ref _particles[slot];
            particle.Age = 0f;
            particle.Velocity = direction * speed;
            particle.SpawnOrder = _spawnCounter++;
            particle.Renderer.sharedMaterial = material;
            particle.Transform.localPosition = origin;
            particle.Transform.localScale = Vector3.one;
            particle.Transform.gameObject.SetActive(true);
        }

        int FindSlot()
        {
            int oldest = 0;
            for (int i = 0; i < Capacity; i++)
            {
                if (_particles[i].Age >= Lifetime) return i;
                if (_particles[i].SpawnOrder < _particles[oldest].SpawnOrder) oldest = i;
            }
            return oldest;
        }

        static Vector3 Direction(int seed, int index, int count)
        {
            // Even angular spread with a small seeded offset; deterministic for a given command index.
            float offset = (seed * 0.618034f) % 1f;
            float angle = (index + offset) / count * Mathf.PI * 2f;
            return new Vector3(Mathf.Cos(angle), 0.6f, Mathf.Sin(angle)).normalized;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            for (int i = 0; i < Capacity; i++)
            {
                ref var particle = ref _particles[i];
                if (particle.Age >= Lifetime) continue;

                particle.Age += dt;
                if (particle.Age >= Lifetime)
                {
                    particle.Transform.gameObject.SetActive(false);
                    continue;
                }
                particle.Transform.localPosition += particle.Velocity * dt;
                particle.Transform.localScale = Vector3.one * (1f - particle.Age / Lifetime);
            }
        }
    }
}
