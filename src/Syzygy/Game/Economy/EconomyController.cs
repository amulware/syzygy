using System;
using System.Linq;
using amulware.Graphics;
using Bearded.Utilities.Input;
using Bearded.Utilities.Math;
using Bearded.Utilities.SpaceTime;
using OpenTK;
using OpenTK.Input;
using Syzygy.Game.Astronomy;
using Syzygy.Game.SyncedCommands;
using Syzygy.Rendering;
using Syzygy.Rendering.Game;
using TimeSpan = Bearded.Utilities.SpaceTime.TimeSpan;

namespace Syzygy.Game.Economy
{
    sealed class EconomyController : GameObject, IPlayerController
    {
        private readonly PlayerGameView view;
        private readonly Player player;
        private readonly IBody body;
        private readonly Game.Economy.Economy economy;
        private readonly EcoStatController[] stats;

        public Player Player { get { return this.player; } }

        private readonly PlayerControls controls;

        private Direction2 aimDirection;
        private Instant lastShot;


        public EconomyController(GameState game, Id<Player> player, PlayerGameView view)
            : base(game)
        {
            this.view = view;
            this.player = game.Players[player];
            this.economy = game.Economies.First(e => e.Player == this.player);
            this.body = this.economy.Body;

            view.FocusOnBody(this.body);

            this.controls = new PlayerControls();

            this.stats = new[]
            {
                new EcoStatController(this.game, this, this.economy, EcoValue.Income,
                    KeyboardAction.FromKey(Key.Number1),KeyboardAction.FromKey(Key.Number2), "1<>2"), 
                new EcoStatController(this.game, this, this.economy, EcoValue.Projectiles,
                    KeyboardAction.FromKey(Key.Q),KeyboardAction.FromKey(Key.W), "Q<>W"), 
                new EcoStatController(this.game, this, this.economy, EcoValue.FireRate,
                    KeyboardAction.FromKey(Key.A),KeyboardAction.FromKey(Key.S), "A<>S"), 
                new EcoStatController(this.game, this, this.economy, EcoValue.Defenses,
                    KeyboardAction.FromKey(Key.Z),KeyboardAction.FromKey(Key.X), "Z<>X"), 
            };
        }

        public override void Update(TimeSpan t)
        {
            var rotation = this.controls.RotateCounterClockwise.AnalogAmount
                - this.controls.RotateClockwise.AnalogAmount;

            this.aimDirection += 2f.Radians() * rotation * (float)t.NumericValue;
    
#if DEBUG
            if (this.controls.ShootDebug.Hit)
            {
                var request = ShootDebugParticleFromPlanet.Request(this.game, this, this.body, this.aimDirection);
                this.game.RequestHandler.TryDo(request);
            }
#endif

            if (this.controls.Shoot.Active)
            {
                var fireInterval = new TimeSpan(1 / this.economy[EcoValue.FireRate].Value);

                var timeSinceLastShot = this.game.Time - this.lastShot;

                if (timeSinceLastShot > fireInterval)
                {
                    if (this.economy[EcoValue.Projectiles].Value >= 1)
                    {
                        var request = ShootProjectileFromPlanet.Request(this.game, this, this.body, this.aimDirection);
                        this.game.RequestHandler.TryDo(request);
                        this.lastShot = this.game.Time;
                    }
                }
            }

            this.updateZoom(t);

            foreach (var stat in this.stats)
            {
                stat.Update(t);
            }
        }

        private void updateZoom(TimeSpan t)
        {
            float zoomDelta = InputManager.DeltaScroll;
            zoomDelta += (float)t.NumericValue * 15
                * (this.controls.ZoomOut.AnalogAmount - this.controls.ZoomIn.AnalogAmount)
                + (this.controls.ZoomOut.Hit ? 1 : 0)
                + (this.controls.ZoomIn.Hit ? -1 : 0);

            if (zoomDelta != 0)
            {
                var zoomFactor = GameMath.Pow(1 / 1.4f, zoomDelta);
                this.view.Zoom *= zoomFactor;
            }
        }

        public override void Draw(GeometryManager geos)
        {
            var geo = geos.Primitives;

            geo.Color = Color.Lime * 0.5f;
            geo.LineWidth = 0.1f;

            var shape = this.body.Shape;
            var center = shape.Center.Vector;

            var r = shape.Radius.NumericValue + 0.2f;

            geo.DrawCircle(center, r, false);

            var dVector = this.aimDirection.Vector;

            geo.LineWidth = 0.05f;
            geo.DrawLine(center + dVector * r, center + dVector * (r + 0.25f));

            this.drawHealthBar(geos);
            this.drawProjectilePathPreview(geo);
            this.drawHud(geos);
        }

        private void drawHud(GeometryManager geos)
        {
            var p = new Vector2(16f, 9f);

            var text = geos.HudText;
            text.Height = 0.5f;

            foreach (var economy in this.game.Economies)
            {
                text.Color = economy.Body.HealthPercentage > 0 ? Color.White : Color.Red;
                text.DrawString(p, string.Format("{0}", economy.Player.Name), 1);

                p += new Vector2(0, -0.5f);
            }

            foreach (var stat in this.stats)
            {
                stat.Draw(geos);
            }

            p = new Vector2(-16f, 9f - 0.5f * this.stats.Count());

            var damageReduction = (1 - this.economy.DamageFactor) * 100;

            text.DrawString(p, string.Format("Damage Reduction: {0:0.0}%", damageReduction));
        }

        private void drawProjectilePathPreview(PrimitiveGeometry geo)
        {
            var s = this.body.Shape;
            var dVector = this.aimDirection.Vector;

            var v = new Velocity2(dVector * 0.9f) + this.body.Velocity;
            var p = s.Center + new Difference2(dVector * s.Radius.NumericValue * 1.5f);

            geo.LineWidth = 0.05f;
            geo.Color = Color.Gray;

            for (int i = 0; i < 50; i++)
            {
                var acceleration = Vector2.Zero;

                foreach (var body in this.game.Bodies)
                {
                    var shape = body.Shape;

                    var difference = shape.Center - p;

                    var distanceSquared = difference.LengthSquared;

                    var a = Constants.G * body.Mass / distanceSquared.NumericValue;

                    var dirNormal = difference.Direction.Vector;

                    acceleration += dirNormal * a;
                }

                var speedFactor = Math.Min(0.5f / acceleration.Length.Sqrted(), 0.5f / v.Speed.NumericValue.Squared());

                var t = TimeSpan.One * speedFactor;


                v += new Velocity2(acceleration * (float)t.NumericValue);

                var p2 = p + v * t;

                geo.DrawLine(p.Vector, p2.Vector);

                p = p2;
            }
        }

        private void drawHealthBar(GeometryManager geos)
        {
            var geo = geos.Primitives;

            var shape = this.body.Shape;
            var center = shape.Center.Vector;
            var r = shape.Radius.NumericValue + 0.2f;

            var p = this.body.HealthPercentage;

            const float barLength = 1.5f;
            var barStart = center + new Vector2(-0.5f * barLength, -r - 0.25f);

            geo.LineWidth = 0.075f;
            geo.Color = Color.Gray;
            geo.DrawLine(barStart, barStart + new Vector2(barLength, 0));

            var healthColor = Color.FromHSVA(p.Squared() * GameMath.Pi * 2 / 3, 0.8f, 0.8f);

            geo.Color = healthColor;
            geo.DrawLine(barStart, barStart + new Vector2(barLength * p, 0));

            var text = geos.GameText;

            text.Color = healthColor;

            text.Height = 0.5f;
            text.DrawString(center + new Vector2(0, -r - 0.3f),
                string.Format("{0:0}%", (int)(p * 100)), 0.5f);
        }
    }
}
