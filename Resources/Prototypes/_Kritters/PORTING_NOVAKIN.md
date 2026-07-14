# Novakin implementation and porting notes

## Current implementation

Novakin are a playable species with a gas-based body. Their selected structural gas is nitrogen by default, or one of oxygen, nitrous oxide, ammonia, carbon dioxide, or water vapor when selected through the Kritters blood-type system.

Each Novakin has a Nexus and a Core. The Core functions as the heart and stomach, processing chemicals, maintains the body's gaseous structure and thermal activity, and acts as the 'furnace' which keeps the Novakin heated when provided with fuel. Novakin do not eat, drink, breathe, or passively heal like other species, they do require refueling as well as occasional charges with their inhalers, they can currently only ingest chems, not be injected with them or be stuck into a cryotube. They can accept topicals.

### Core reserve and temperature

- The gas reserve is displayed only through the Novakin battery-style HUD alert.
- Reserve leaks into the local atmosphere as the selected native gas. Leakage slows in pressure-protective outerwear, then becomes severe below the critical reserve threshold or while medically critical.
- Below the damage threshold, depleted reserve causes Cellular damage and weakens thermal regulation.
- Native thermal regulation targets 373.15 K (100 C) while Fuel remains. Empty Fuel causes accelerated cooling.
- Fuel consumption rises with temperature, reaching its maximum rate at 700 K. Movement speed also rises with core temperature.
- At 650 K or hotter, **Discharge Core** vents half of the remaining reserve, releases that gas into the surrounding atmosphere, and lowers core temperature by 300 K. It is an emergency cooling action, not healing.
- Novakin are immune to Heat damage below 700 K, but past it they begin to take it at the standard rate as other species. Novakin are immune to Poison, Shock, Caustic, and Asphyxiation. They are 60% resistant to radiation, and are 50% more subseptible to cold damage. Barotrauma damage is reduced by 15%. Temperature and cellular damage occuring at 368.15k and below. Their heat-generated glow is hidden only while both a pressure-protective outer suit and helmet are worn.

### Equipment and character creation

Novakin loadouts use the same species-gated pattern as sheleg for the most part. They receive a pressure-protective EVA suit, and their survival kits contain adaptive inhalers for their chosen gas. These kits and inhalers are restricted to Novakin.

An adaptive starting inhaler is configured after spawn for the character's selected gas. Inhalers restore reserve only when their pure gas matches the target Novakin's gas; they can be refilled from an appropriate pure-gas tank or canister.

## Repository layout

Novakin content follows the repository's normal subsystem layout rather than living in one special `Novakin` folder:

- Code is in `Content.{Shared,Server,Client}/_Kritters/` under ordinary `Components`, `Systems`, `Overlays`, `Visuals`, `Prototypes`, and `BloodTypes` categories.
- Prototypes are distributed with their domains under `Resources/Prototypes/_Kritters/`: `Actions`, `Alerts`, `Atmos`, `Body`, `Damage`, `Entities`, `Needs`, `Species`, `Voice`, and related categories.
- Localisation is distributed under `Resources/Locale/en-US/_Kritters/` by domain.
- Species-specific art and audio use the same conventional per-species asset locations as the other species: `Mobs/Species/Novakin`, `Mobs/Customization/Novakin`, and `Voice/Novakin`.
- The guidebook entry is `Resources/ServerInfo/Guidebook/Mobs/_Kritters/Novakin.xml`.

Any required integration outside `_Kritters` is marked with a `Kritters` comment. These seams include the health analyzer and medical HUD, thermal-regulator access, character-loadout filters, species registry, and the Kritters blood-type selector.

## Kritters blood-type adapter

`Content.Server/_Kritters/BloodTypes/NovakinBloodTypeAdapterSystem.cs` translates this repository's blood-type profile selection into the portable `NovakinGasPrototype`. The six selection entries are in `Resources/Prototypes/_Kritters/BloodTypes/blood_types.yml`; their compatibility tags are in `Resources/Prototypes/_Kritters/tags.yml`.

Forks without this blood selector can omit the adapter. Novakin will then use nitrogen by default. A replacement character-creation adapter must call `SharedNovakinPhysiologySystem.SetGas` after spawn and configure any adaptive starting inhalers after loadout containers have been created.

## Source provenance and attribution

- [Triad Sector PR #96](https://github.com/Triad-Sector/Triad_Sector/pull/96) supplied the Shadow/Shadekin implementation that originally informed Novakin.
- [TheDen PR #1554](https://github.com/TheDenSS14/TheDen/pull/1554) supplied the Shadow/Shadekin voice assets used by Novakin.

Kritters renamed imported Shadow/Shadekin assets, prototypes, and paths to `Novakin` for repository-wide consistency. The rename does not change authorship or licensing. Per-asset attribution manifests in `Resources/Audio/_Kritters/Voice/Novakin/` preserve the original creator and source credits.

The unused thermal-vision effects, clothing-based night-vision extension, and unused inherited organ sprites from that basis have been removed. Current Novakin night vision, glow, overlays, voice assets, markings, gas core, inhalers, and pressure-equipment mechanics are active.

## Verification

- Load all prototypes and create one Novakin for each supported gas selection.
- Confirm that missing or invalid profile selection defaults to nitrogen.
- Confirm that only the Nexus and Core are present.
- Confirm that normal examine text does not reveal the selected gas, while a health analyzer does.
- Confirm that the battery alert tracks reserve and that there is no passive healing.
- Confirm the 373.15 K regulation target, Fuel-depletion cooling, pressure-suit leak reduction, critical-reserve collapse, and low-reserve Cellular damage.
- Confirm adaptive survival-kit inhalers match the selected gas and reject mismatched gas.
- Confirm Discharge Core only works at 650 K or higher, vents half the reserve, and removes 300 K.
- Confirm night vision, flash sensitivity, glow suppression by a sealed suit and helmet, as well as resistances and weaknesses.
