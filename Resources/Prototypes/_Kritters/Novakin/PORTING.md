# Porting Novakin

Novakin is organized as a self-contained module under the `_Kritters/Novakin` directories in `Content.*` and `Resources`. Copy those directories first, then apply the small integration changes listed below.

## Source provenance and attribution

- [Triad Sector PR #96](https://github.com/Triad-Sector/Triad_Sector/pull/96) provided the Shadow/Shadekin implementation that served as the basis for the Novakin creature.
- [TheDen PR #1554](https://github.com/TheDenSS14/TheDen/pull/1554) provided the Shadow/Shadekin voice assets used by Novakin.

Kritters renamed the imported Shadow/Shadekin assets, prototypes, and paths to `Novakin` strictly for streamlining and repository-wide consistency, so the species is identified correctly throughout this module. The rename does not change the original authorship or licensing; the per-asset attribution manifests under `Resources/Audio/_Kritters/Novakin/Voice/` retain the original creator and source credits.

## Required integration

- Register `Novakin` in the destination fork's species guide and character-creation species list.
- Add `NovakinBoxSurvival` to the destination fork's species-filtered survival loadout groups.
- Move or adapt the shared flash-immunity changes used by `KrittersNightVision` and `FlashModifier`.
- Confirm the destination provides `Temperature`, `ThermalRegulator`, `TemperatureProtection`, atmos tile mixtures, native gas tanks/canisters, and the standard humanoid body/organ contracts.
- Keep prototype IDs unchanged unless every reference is updated together.

## Optional Kritters adapter

`Content.Server/_Kritters/Novakin/Adapters/KrittersBloodTypes` maps this repository's blood-type profile choice to the portable `NovakinGasPrototype`. The six adapter entries live in `_Kritters/BloodTypes/blood_types.yml` and their compatibility tags live in `_Kritters/tags.yml`.

Forks without the Kritters blood selector can omit the adapter. Novakin then default to nitrogen. A replacement adapter only needs to call `SharedNovakinPhysiologySystem.SetGas` after character creation or spawn.

If adaptive starting inhalers are used, the replacement adapter should also call `ConfigureStartingInhalers` or implement the equivalent container traversal after loadout equipment has spawned.

## Verification

- Load all prototypes and create one Novakin for each gas identity.
- Verify nitrogen is used for a missing or invalid selection.
- Verify only the Nexus and Core are present.
- Verify native thermal regulation attempts to return the Novakin to 373.15 K.
- Verify insulated outerwear reduces cooling enough for thermal regulation to recover more effectively.
- Verify an exposed Novakin can cool faster than they regulate, takes Cold damage, and heats the local atmosphere.
- Verify each inhaler accepts only its configured pure gas, restores matching reserve, and can be refilled as a native gas tank.
- Verify the survival box's adaptive inhaler matches the character's selected gas after spawning.
- Verify Heat immunity, Radiation resistance, night vision, and flash sensitivity.

## recent ideas/additions

- Novakin will need to consume radioactive, carbon, or hydrogen based materials to be able to maintain their cores ability to generate heat and retain gas. They must stoke themselves at a similar rate to other species who need to eat.
- Gas replenishment should be done at a similar rate to how other species need to hydrate themselves.
- They will also be able to use alcohol to increase their body temperature, and the higher their temperature is, the closer to 700k they get, the more efficient they are with their gas in terms of not losing it as much. However their body still regulates back down to the normal 373k temperature, but it becomes slower the hotter they get. However, as a result of the higher temperature they get significantly hungrier for more fuel. If they run out of fuel they start to rapidly cool down.
- The hotter they get the more an orange aura or glow begins to form around the edges of the players screen. They also begin to move faster and are more resistant to damage, up to 25% for brute damage, but will make them take 50% more cold damage.
- At 650k there will be a 'discharge' or 'release' ability which will lower the Novakins temperature by 300k by venting gas into the area surrounding them.
