using System;
using IceCoast.Simulation.Commands;
using IceCoast.Simulation.Finance;
using IceCoast.View.UI.Toolkit;

namespace IceCoast.View.UI.Windows
{
    /// <summary>Cash, ticket price, and today's books by category, plus the last week's profit.</summary>
    public sealed class FinanceWindow : UiWindow
    {
        public const string WindowKey = "finance";
        private readonly GameUi _game;

        public FinanceWindow(GameUi game) : base(WindowKey, "Finances", 250)
        {
            _game = game;
            Scheme = UiTheme.Grey;
        }

        public override void DrawContent(UiContext ui)
        {
            var sim = _game.Sim;
            if (sim == null) return;
            var econ = sim.World.Economy;
            var today = econ.Today;
            var red = GameUi.SeverityOnLight(Simulation.Events.EventSeverity.Danger);
            var green = GameUi.SeverityOnLight(Simulation.Events.EventSeverity.Good);

            ui.Field("Cash", _game.CashText, econ.Cash < 0 && !sim.Config.SandboxMode ? red : ui.Scheme.Text);
            int price = ui.Counter("Day ticket price", econ.TicketPrice, 0, 500, 5, "$" + econ.TicketPrice,
                                   $"Guests think about ${sim.ArrivalSystem.LastFairPrice:0} is fair. Higher prices mean fewer guests.");
            if (price != econ.TicketPrice) _game.Runner.Submit(new SetTicketPriceCommand(price));

            ui.Separator();
            ui.Heading("Today's income");
            int shown = 0;
            foreach (LedgerCategory cat in Enum.GetValues(typeof(LedgerCategory)))
            {
                long v = today.Income[(int)cat];
                if (v == 0) continue;
                ui.Field(Pretty(cat), _game.Money(v));
                shown++;
            }
            if (shown == 0) ui.Label("Nothing yet.", ui.Scheme.Text);

            ui.Heading("Today's expenses");
            shown = 0;
            foreach (LedgerCategory cat in Enum.GetValues(typeof(LedgerCategory)))
            {
                long v = today.Expenses[(int)cat];
                if (v == 0) continue;
                ui.Field(Pretty(cat), _game.Money(-v), red);
                shown++;
            }
            if (shown == 0) ui.Label("Nothing yet.", ui.Scheme.Text);
            ui.Separator();
            ui.Field("Profit today", _game.Money(today.Profit), today.Profit >= 0 ? green : red);

            if (econ.History.Count > 0)
            {
                ui.Separator();
                ui.Heading("Recent days");
                for (int i = econ.History.Count - 1; i >= 0 && i >= econ.History.Count - 7; i--)
                {
                    var d = econ.History[i];
                    ui.Field($"Day {d.Day + 1}: {d.GuestsArrived:N0} guests", _game.Money(d.Profit), d.Profit >= 0 ? green : red);
                }
            }
        }

        private static string Pretty(LedgerCategory c)
        {
            switch (c)
            {
                case LedgerCategory.LiftOperations: return "Lift operations";
                case LedgerCategory.TreeClearing: return "Tree clearing";
                case LedgerCategory.FoodService: return "Food service";
                default: return c.ToString();
            }
        }
    }
}
