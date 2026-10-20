using IceCoast.Simulation.Buildings;
using IceCoast.Simulation.Commands;
using IceCoast.Simulation.Events;
using IceCoast.View.UI.Toolkit;

namespace IceCoast.View.UI.Windows
{
    /// <summary>
    /// One building (a Waffle Cabin): the price of a waffle, open or closed, the line, today's sales and profit,
    /// and demolish. One window per building.
    /// </summary>
    public sealed class BuildingWindow : UiWindow
    {
        private readonly GameUi _game;
        private readonly int _buildingId;
        private bool _confirmDemolish;

        public static string KeyFor(int buildingId) => "building:" + buildingId;

        public BuildingWindow(GameUi game, int buildingId) : base(KeyFor(buildingId), "Building", 236)
        {
            _game = game;
            _buildingId = buildingId;
            Scheme = UiTheme.Brown;
        }

        private Building Building => _game.Sim?.BuildingSystem.GetBuilding(_buildingId);

        public override string Caption => Building?.Name ?? "Building";
        public override string CaptionAction => "inspect";
        public override string CaptionActionTooltip => "Centre the view on this building";
        public override void OnCaptionAction()
        {
            var b = Building;
            if (b != null) _game.Focus(b.Origin);
        }

        public override bool StillValid() => Building != null;

        public override void OnClosed()
        {
            if (_game.View.SelectedBuildingId == _buildingId) _game.View.SelectedBuildingId = 0;
        }

        public override void DrawContent(UiContext ui)
        {
            var b = Building;
            if (b == null) return;
            var sim = _game.Sim;
            var system = sim.BuildingSystem;
            var def = system.Def(b);
            var good = GameUi.SeverityOnLight(EventSeverity.Good);
            var warn = GameUi.SeverityOnLight(EventSeverity.Warning);
            var bad = GameUi.SeverityOnLight(EventSeverity.Danger);

            string status = !b.PlayerOpen ? "Closed" : system.IsServing(b) ? "Serving" : "Closed for the night";
            ui.Field(def.DisplayName, status, b.PlayerOpen && system.IsServing(b) ? good : UiTheme.TextBlack);

            int price = ui.Counter("Waffle price", b.Price, 0, SetBuildingPriceCommand.MaxPrice, 1, "$" + b.Price,
                                   $"Guests think about ${def.FairPrice} is fair and nobody pays ${def.MaxPrice}. " +
                                   "Cheaper waffles make guests happier; dearer ones put some off.");
            if (price != b.Price) _game.Runner.Submit(new SetBuildingPriceCommand(b.Id, price));
            ui.Field("Happiness per waffle", $"+{BuildingSystem.HappinessFor(def, b.Price):0.#}");

            ui.Separator();
            float wait = system.EstimatedWaitMinutes(b);
            ui.Field("In line", $"{b.Queue.Count} (~{wait:0} min wait)", wait > sim.Config.Food.MaxWaitMinutes * 0.75f ? warn : ui.Scheme.Text);
            ui.Field("Sold today", $"{b.SoldToday:N0} ({_game.Money(b.RevenueToday)})");
            if (b.PricedOutToday > 0) ui.Field("Too pricey for", $"{b.PricedOutToday:N0} hungry guests", warn);
            long profit = b.RevenueToday - b.CostToday;
            ui.Field("Profit today", _game.Money(profit), profit >= 0 ? ui.Scheme.Text : bad);
            ui.Field("Sold all season", $"{b.SoldTotal:N0} ({_game.Money(b.RevenueTotal)})");
            ui.Field("Running cost", $"${def.OperatingCostPerHour}/hr open, ${def.IngredientCostPerSale} a waffle");

            ui.Space(2);
            var row = ui.Row(UiContext.ButtonHeight);
            if (ui.Button(UiContext.Cell(row, 0, 2), b.PlayerOpen ? "Close" : "Open"))
                _game.Runner.Submit(new SetBuildingOpenCommand(b.Id, !b.PlayerOpen));
            if (!_confirmDemolish)
            {
                if (ui.Button(UiContext.Cell(row, 1, 2), "Demolish...")) _confirmDemolish = true;
            }
            else
            {
                var cell = UiContext.Cell(row, 1, 2);
                if (ui.Button(UiContext.Cell(cell, 0, 2), "Yes!", true, false, "Knock it down for good (no refund)"))
                {
                    _game.Runner.Submit(new DemolishBuildingCommand(b.Id));
                    _game.View.ClearSelection();
                }
                if (ui.Button(UiContext.Cell(cell, 1, 2), "No")) _confirmDemolish = false;
            }
        }
    }
}
