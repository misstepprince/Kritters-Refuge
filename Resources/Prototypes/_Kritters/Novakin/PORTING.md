# Porting Novakin

Novakin is organized as a self-contained module under the `_Kritters/Novakin` directories in `Content.*` and `Resources`. Copy those directories first, then apply the small integration changes listed below.

## Required integration

- Register `Novakin` in the destination fork's species guide and character-creation species list.
- Move or adapt the shared flash-immunity changes used by `KrittersNightVision` and `FlashModifier`.
- Confirm the destination provides `Temperature`, `ThermalRegulator`, `PressureProtection`, atmos tile mixtures, and the standard humanoid body/organ contracts.
- Keep prototype IDs unchanged unless every reference is updated together.

## Optional Kritters adapter

`Content.Server/_Kritters/Novakin/Adapters/KrittersBloodTypes` maps this repository's blood-type profile choice to the portable `NovakinGasPrototype`. The six adapter entries live in `_Kritters/BloodTypes/blood_types.yml` and their compatibility tags live in `_Kritters/tags.yml`.

Forks without the Kritters blood selector can omit the adapter. Novakin then default to nitrogen. A replacement adapter only needs to call `SharedNovakinPhysiologySystem.SetGas` after character creation or spawn.

## Verification

- Load all prototypes and create one Novakin for each gas identity.
- Verify nitrogen is used for a missing or invalid selection.
- Verify only the Nexus and Core are present.
- Verify pressure-protected outerwear stabilizes temperature at 373.15 K.
- Verify an unprotected Novakin cools, takes Cold damage, and heats the local atmosphere.
- Verify Heat immunity, Radiation resistance, night vision, and flash sensitivity.
