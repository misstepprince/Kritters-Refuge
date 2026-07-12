# Porting Novakin

Novakin is organized as a self-contained module under the `_Kritters/Novakin` directories in `Content.*` and `Resources`. Copy those directories first, then apply the small integration changes listed below.

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
