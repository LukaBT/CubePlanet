namespace CubePlanet;

/// <summary>
/// Game logic of a block, selected by name in the definition ("behavior": "door"). Adding content = a definition; a new class is only needed
/// when no existing behaviour fits. Multiblock placing / breaking and opening a block's UI (ui_id) are handled generically in BlockActions.
/// </summary>
public abstract class BlockBehavior
{
    /// <summary>After the block (all multiblock parts) was placed. p = the part that was placed by the player.</summary>
    public virtual void OnPlace(BlockPos p, int id) { }
    /// <summary>Before the block is removed (state = its state word).</summary>
    public virtual void OnBreak(BlockPos p, int id, int state) { }
    /// <summary>Right click. Return true when handled (nothing gets placed). Blocks with a ui_id open it when this returns false.</summary>
    public virtual bool OnUse(BlockPos p, int id, int state) => false;
    /// <summary>Does right-clicking do something (so holding a block does not place it here)?</summary>
    public virtual bool Usable => false;
    /// <summary>10 times per second for every block entity of this block.</summary>
    public virtual void OnTick(BlockEntity e, double dt) { }
    /// <summary>A neighbouring cell changed (placed / broken by a player or another block).</summary>
    public virtual void OnNeighborChanged(BlockPos p, int id, BlockPos from) { }
    /// <summary>May the block be placed at 'at'? (seeds only on farmland). why = the message shown when not.</summary>
    public virtual bool CanPlaceAt(BlockPos at, int id, out string why) { why = null; return true; }
    /// <summary>Items the player gets when breaking it (crops: produce and seeds); null = nothing.</summary>
    public virtual IEnumerable<ItemStack> Harvest(BlockPos p, int id, int state) => null;
    /// <summary>Extra text for the HUD's TARGET line (machines: mode, flow, power, charge).</summary>
    public virtual string Describe(BlockEntity e) => "";

    static readonly Dictionary<string, Func<BlockBehavior>> Known = new()
    {
        ["door"] = () => new DoorBehavior(),
        ["bed"] = () => new BedBehavior(),
        ["container"] = () => new ContainerBehavior(),
        ["smelting"] = () => new SmeltingBehavior(),
        ["oxygen_generator"] = () => new OxygenGeneratorBehavior(),
        ["stairs"] = () => new StairsBehavior(),
        ["connecting"] = () => new ConnectingBehavior(),
        ["turbine"] = () => new TurbineBehavior(),
        ["pump"] = () => new PumpBehavior(),
        ["battery"] = () => new BatteryBehavior(),
        ["spring"] = () => new SpringBehavior(),
        ["drain"] = () => new DrainBehavior(),
        ["farmland"] = () => new FarmlandBehavior(),
        ["wind_turbine"] = () => new WindTurbineBehavior(),
        ["shaft"] = () => new ShaftBehavior(),
        ["generator"] = () => new GeneratorBehavior(),
        ["coal_generator"] = () => new CoalGeneratorBehavior(),
        ["transformer"] = () => new TransformerBehavior(),
        ["lamp"] = () => new LampBehavior(),
        ["electric_furnace"] = () => new ElectricFurnaceBehavior(),
        ["motor"] = () => new MotorBehavior(),
        ["solar_panel"] = () => new SolarPanelBehavior(),
        ["gearbox"] = () => new GearboxBehavior(),
        ["processor"] = () => new ProcessorBehavior(),
        ["industry"] = () => new IndustryBehavior(),
        ["air_separator"] = () => new AirSeparatorBehavior(),
        ["mechanical_pump"] = () => new MechanicalPumpBehavior(),
        ["tank_valve"] = () => new TankValveBehavior(),
        ["compressor"] = () => new CompressorBehavior(),
        ["pipe_valve"] = () => new PipeValveBehavior(),
        ["miner"] = () => new MinerBehavior(),
        ["electrolyzer"] = () => new ElectrolyzerBehavior(),
        ["hydrogen_tank"] = () => new GasTankBehavior(),
        ["gas_tank"] = () => new GasTankBehavior(),
        ["fuel_cell"] = () => new FuelCellBehavior(),
        ["charger"] = () => new ChargerBehavior(),
        ["sprinkler"] = () => new SprinklerBehavior(),
        ["elevator"] = () => new ElevatorBehavior(),
        ["elevator_stop"] = () => new ElevatorStopBehavior(),
        ["elevator_car"] = () => new ElevatorCarBehavior(),
        ["extractor"] = () => new ExtractorBehavior(),
        ["conveyor"] = () => new ConveyorBehavior(),
        ["status_lamp"] = () => new StatusLampBehavior(),
        ["crop"] = () => new CropBehavior(),
    };
    public static BlockBehavior Create(string name, string owner) =>
        Known.TryGetValue(name, out var f) ? f() : throw new InvalidDataException($"{owner}: unknown behavior '{name}' (known: {string.Join(", ", Known.Keys)})");
}
