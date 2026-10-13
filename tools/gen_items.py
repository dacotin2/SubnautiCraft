"""Generates SubnautiCraft's Subnautica items for both mods from the table below (the one place they are listed).

    python tools/gen_items.py

Writes, for the Fabric mod: SnItems.java (registration + creative tab), item model and item definition JSONs,
placeholder textures, en_us/es_es names and the crafting recipes (Subnautica's own fabricator recipes, as shapeless
Minecraft recipes); for the BepInEx plugin: SnItemTable.cs (TechType -> item id, and the recipes, which the plugin
checks against the game's own at runtime).

The placeholders are simple drawings of ours. In game, the plugin draws each item's real Subnautica icon from the
player's own copy of the game and sends it to Minecraft, which keeps them in a resource pack on that computer: no
Subnautica art is stored in this repository.
"""
import json
import os

from PIL import Image, ImageDraw

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..")
MC = os.path.join(ROOT, "mc", "src", "main")
NS = "subnauticraft"

# id, Subnautica TechType, English, Spanish, placeholder colour, shape
ITEMS = [
    ("titanium", "Titanium", "Titanium", "Titanio", (190, 196, 204), "chunk"),
    ("copper_ore", "Copper", "Copper Ore", "Mineral de cobre", (196, 112, 64), "chunk"),
    ("silver_ore", "Silver", "Silver Ore", "Mineral de plata", (214, 218, 224), "chunk"),
    ("gold", "Gold", "Gold", "Oro", (240, 196, 60), "chunk"),
    ("lead", "Lead", "Lead", "Plomo", (110, 116, 130), "chunk"),
    ("quartz", "Quartz", "Quartz", "Cuarzo", (232, 236, 240), "crystal"),
    ("diamond", "Diamond", "Diamond", "Diamante", (150, 236, 240), "crystal"),
    ("lithium", "Lithium", "Lithium", "Litio", (180, 200, 220), "crystal"),
    ("magnetite", "Magnetite", "Magnetite", "Magnetita", (70, 70, 80), "chunk"),
    ("ruby", "AluminumOxide", "Ruby", "Rubí", (220, 40, 70), "crystal"),
    ("kyanite", "Kyanite", "Kyanite", "Cianita", (60, 110, 220), "crystal"),
    ("nickel_ore", "Nickel", "Nickel Ore", "Mineral de níquel", (160, 170, 150), "chunk"),
    ("uraninite_crystal", "UraniniteCrystal", "Uraninite Crystal", "Cristal de uraninita", (120, 220, 80), "crystal"),
    ("crystalline_sulfur", "Sulphur", "Crystalline Sulfur", "Azufre cristalino", (240, 220, 60), "crystal"),
    ("salt_deposit", "Salt", "Salt Deposit", "Depósito de sal", (245, 245, 240), "chunk"),
    ("scrap_metal", "ScrapMetal", "Metal Salvage", "Chatarra", (130, 120, 110), "plate"),
    ("table_coral_sample", "JeweledDiskPiece", "Table Coral Sample", "Muestra de coral de mesa", (230, 120, 90), "organic"),
    ("coral_tube_sample", "CoralChunk", "Coral Tube Sample", "Muestra de coral tubular", (240, 200, 180), "organic"),
    ("acid_mushroom", "AcidMushroom", "Acid Mushroom", "Seta ácida", (120, 200, 90), "organic"),
    ("creepvine_sample", "CreepvinePiece", "Creepvine Sample", "Muestra de enredadera", (200, 170, 70), "organic"),
    ("creepvine_seed_cluster", "CreepvineSeedCluster", "Creepvine Seed Cluster", "Racimo de semillas de enredadera", (250, 220, 90), "organic"),
    ("gel_sack", "JellyPlant", "Gel Sack", "Saco de gel", (230, 120, 200), "organic"),
    ("stalker_tooth", "StalkerTooth", "Stalker Tooth", "Diente de acechador", (240, 236, 220), "crystal"),
    ("deep_shroom", "WhiteMushroom", "Deep Shroom", "Seta abisal", (220, 220, 230), "organic"),
    ("blood_oil", "BloodOil", "Blood Oil", "Aceite de sangre", (170, 30, 40), "organic"),
    ("ion_cube", "PrecursorIonCrystal", "Ion Cube", "Cubo de iones", (80, 240, 160), "crystal"),
    ("titanium_ingot", "TitaniumIngot", "Titanium Ingot", "Lingote de titanio", (200, 206, 214), "ingot"),
    ("glass", "Glass", "Glass", "Vidrio", (190, 230, 240), "plate"),
    ("enameled_glass", "EnameledGlass", "Enameled Glass", "Vidrio esmaltado", (150, 200, 230), "plate"),
    ("copper_wire", "CopperWire", "Copper Wire", "Cable de cobre", (210, 120, 60), "wire"),
    ("wiring_kit", "WiringKit", "Wiring Kit", "Kit de cableado", (200, 200, 210), "kit"),
    ("advanced_wiring_kit", "AdvancedWiringKit", "Advanced Wiring Kit", "Kit de cableado avanzado", (240, 200, 70), "kit"),
    ("computer_chip", "ComputerChip", "Computer Chip", "Chip de ordenador", (60, 160, 90), "kit"),
    ("battery", "Battery", "Battery", "Batería", (90, 160, 220), "ingot"),
    ("power_cell", "PowerCell", "Power Cell", "Célula de energía", (70, 130, 210), "ingot"),
    ("fiber_mesh", "FiberMesh", "Fiber Mesh", "Malla de fibra", (210, 190, 120), "plate"),
    ("lubricant", "Lubricant", "Lubricant", "Lubricante", (240, 200, 80), "bottle"),
    ("silicone_rubber", "Silicone", "Silicone Rubber", "Caucho de silicona", (240, 220, 160), "plate"),
    ("plasteel_ingot", "PlasteelIngot", "Plasteel Ingot", "Lingote de plastiacero", (170, 180, 200), "ingot"),
    ("synthetic_fibers", "AramidFibers", "Synthetic Fibers", "Fibras sintéticas", (220, 220, 200), "plate"),
    ("benzene", "Benzene", "Benzene", "Benceno", (200, 230, 120), "bottle"),
    ("hydrochloric_acid", "HydrochloricAcid", "Hydrochloric Acid", "Ácido clorhídrico", (150, 230, 150), "bottle"),
    ("polyaniline", "Polyaniline", "Polyaniline", "Polianilina", (60, 80, 170), "bottle"),
    ("aerogel", "Aerogel", "Aerogel", "Aerogel", (200, 230, 250), "plate"),
    ("bleach", "Bleach", "Bleach", "Lejía", (230, 240, 250), "bottle"),
    # tools: they run on a battery (the durability bar is its charge); crafting one with a battery recharges it
    ("flashlight", "Flashlight", "Flashlight", "Linterna", (230, 200, 60), "tool", {"charge": 600}),
    ("seaglide", "Seaglide", "Seaglide", "Deslizador", (240, 170, 40), "tool", {"charge": 480}),
    ("scanner", "Scanner", "Scanner", "Escáner", (90, 200, 230), "tool", {"charge": 600}),
    # right click heals (Subnautica's 50 health = Minecraft's 10)
    ("first_aid_kit", "FirstAidKit", "First Aid Kit", "Botiquín", (230, 60, 60), "medkit", {"heal": 10}),
    # vehicles: only their pictures, for the mobile vehicle bay's Minecraft screen (the bay builds the real thing)
    ("seamoth", "Seamoth", "Seamoth", "Seamoth", (230, 230, 220), "tool", {"icon": True}),
    ("prawn_suit", "Exosuit", "Prawn Suit", "Exotraje PRAWN", (240, 200, 60), "tool", {"icon": True}),
    ("cyclops", "Cyclops", "Cyclops", "Cíclope", (200, 200, 200), "tool", {"icon": True}),
    ("rocket_base", "RocketBase", "Neptune Launch Platform", "Plataforma de lanzamiento Neptuno", (220, 220, 230), "tool", {"icon": True}),
]

# Subnautica's small fish: caught with right click, eaten raw, cooked in a furnace (its fabricator) or cured with salt.
# Minecraft food: raw like cod, cooked worth Subnautica's cooked food value / 6, cured a little less but more filling.
FISH = [
    ("peeper", "Peeper", "Peeper", "Mirón", (240, 170, 60), 5),
    ("bladderfish", "Bladderfish", "Bladderfish", "Pez vejiga", (230, 200, 160), 3),
    ("garryfish", "GarryFish", "Garryfish", "Pez Garry", (170, 150, 120), 4),
    ("holefish", "HoleFish", "Holefish", "Pez agujero", (120, 180, 140), 4),
    ("boomerang", "Boomerang", "Boomerang", "Bumerán", (230, 120, 160), 4),
    ("hoopfish", "Hoopfish", "Hoopfish", "Pez aro", (90, 150, 230), 4),
    ("reginald", "Reginald", "Reginald", "Reginald", (110, 120, 200), 7),
    ("spadefish", "Spadefish", "Spadefish", "Pez pala", (200, 200, 120), 4),
    ("eyeye", "Eyeye", "Eyeye", "Ojojo", (160, 120, 220), 3),
    ("oculus", "Oculus", "Oculus", "Óculo", (90, 90, 160), 5),
    ("hoverfish", "Hoverfish", "Hoverfish", "Pez flotante", (120, 220, 200), 4),
    ("spinefish", "Spinefish", "Spinefish", "Pez espina", (150, 170, 190), 3),
    ("magmarang", "LavaBoomerang", "Magmarang", "Magmarán", (240, 90, 40), 4),
    ("red_eyeye", "LavaEyeye", "Red Eyeye", "Ojojo rojo", (230, 60, 60), 3),
]
for _id, _tech, _en, _es, _colour, _cooked in FISH:
    ITEMS.append((_id, _tech, _en, _es, _colour, "fish", {"food": (2, 0.1)}))
    ITEMS.append(("cooked_" + _id, "Cooked" + _tech, "Cooked " + _en, _es + " cocinado", tuple(int(v * 0.75) for v in _colour), "fish", {"food": (_cooked, 0.6)}))
    ITEMS.append(("cured_" + _id, "Cured" + _tech, "Cured " + _en, _es + " curado", tuple(min(255, int(v * 0.8 + 40)) for v in _colour), "fish", {"food": (max(2, _cooked - 1), 0.8)}))

# Subnautica's fabricator recipes: result, how many, ingredients. Minecraft's grid holds 9 items, so the titanium
# ingot takes 9 titanium (10 in Subnautica). Lubricant and silicone rubber are both one creepvine seed cluster in
# Subnautica; Minecraft can't tell two identical recipes apart, so lubricant also takes a glass bottle to fill.
RECIPES = [
    ("titanium", 4, ["scrap_metal"]),
    ("titanium_ingot", 1, ["titanium"] * 9),
    ("glass", 1, ["quartz"] * 2),
    ("enameled_glass", 1, ["glass", "stalker_tooth"]),
    ("copper_wire", 1, ["copper_ore"] * 2),
    ("wiring_kit", 1, ["silver_ore"] * 2),
    ("advanced_wiring_kit", 1, ["wiring_kit", "gold", "gold", "computer_chip"]),
    ("computer_chip", 1, ["table_coral_sample", "table_coral_sample", "gold", "copper_wire"]),
    ("battery", 1, ["acid_mushroom", "acid_mushroom", "copper_ore"]),
    ("power_cell", 1, ["battery", "battery", "silicone_rubber"]),
    ("fiber_mesh", 1, ["creepvine_sample"] * 2),
    ("silicone_rubber", 1, ["creepvine_seed_cluster"]),
    ("lubricant", 1, ["creepvine_seed_cluster", "minecraft:glass_bottle"]),
    ("plasteel_ingot", 1, ["titanium_ingot", "lithium", "lithium"]),
    ("synthetic_fibers", 1, ["fiber_mesh", "benzene"]),
    ("benzene", 1, ["blood_oil"] * 3),
    ("hydrochloric_acid", 1, ["deep_shroom"] * 3 + ["salt_deposit"]),
    ("polyaniline", 1, ["gold", "hydrochloric_acid"]),
    ("aerogel", 1, ["ruby", "gel_sack"]),
    ("bleach", 1, ["salt_deposit", "coral_tube_sample"]),
    ("flashlight", 1, ["battery", "glass"]),
    ("seaglide", 1, ["battery", "lubricant", "copper_wire", "titanium"]),
    ("scanner", 1, ["battery", "titanium"]),
    ("first_aid_kit", 1, ["creepvine_sample"]),
]
# always craftable, whatever the diver knows (SnBlueprints): salvage into titanium, recharging a tool
ALWAYS = ["titanium"]
# a fresh battery into a tool: it comes out fully charged (Subnautica swaps batteries); not a Subnautica recipe
RECHARGE = [("flashlight", 1, ["flashlight", "battery"]), ("seaglide", 1, ["seaglide", "battery"]), ("scanner", 1, ["scanner", "battery"])]
for _id, *_ in FISH:
    RECIPES.append(("cured_" + _id, 1, [_id, "salt_deposit"]))
# raw fish -> cooked fish in a furnace, smoker or on a campfire (Subnautica's fabricator cooks them)
COOKING = [(_id, "cooked_" + _id) for _id, *_ in FISH]


# the vehicle bay's Minecraft screen
UI = {
    "en_us": {"subnauticraft.bay.needs": "Needs:", "subnauticraft.bay.click": "Click to build it",
              "subnauticraft.bay.missing": "Not enough materials", "subnauticraft.bay.building": "Building: %s",
              "subnauticraft.bay.blocked": "The vehicle bay has no room to build it here",
              "advancements.subnauticraft.root.title": "Subnautica",
              "advancements.subnauticraft.root.description": "Subnautica's blueprints: scan fragments with the scanner to learn more",
              "advancements.subnauticraft.blueprint": "Blueprint learned: it can be crafted now",
              "advancements.subnauticraft.vehicle": "Blueprint learned: the mobile vehicle bay can build it"},
    "es_es": {"subnauticraft.bay.needs": "Necesita:", "subnauticraft.bay.click": "Haz clic para construirlo",
              "subnauticraft.bay.missing": "Faltan materiales", "subnauticraft.bay.building": "Construyendo: %s",
              "subnauticraft.bay.blocked": "El creador de vehículos no tiene sitio para construirlo aquí",
              "advancements.subnauticraft.root.title": "Subnautica",
              "advancements.subnauticraft.root.description": "Los planos de Subnautica: escanea fragmentos con el escáner para aprender más",
              "advancements.subnauticraft.blueprint": "Plano aprendido: ya se puede fabricar",
              "advancements.subnauticraft.vehicle": "Plano aprendido: el creador de vehículos móvil puede construirlo"},
}


def write(path, text):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write(text)


def write_json(path, obj):
    write(path, json.dumps(obj, indent=2, ensure_ascii=False) + "\n")


def placeholder(colour, shape):
    """A 16 x 16 Minecraft-style stand-in, drawn here (the real icon comes from the player's Subnautica)."""
    img = Image.new("RGBA", (16, 16), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    c = colour + (255,)
    dark = tuple(max(0, int(v * 0.55)) for v in colour) + (255,)
    light = tuple(min(255, int(v * 1.25 + 30)) for v in colour) + (255,)
    if shape == "chunk":
        d.polygon([(3, 6), (7, 2), (12, 3), (14, 8), (11, 13), (5, 14), (2, 10)], fill=c, outline=dark)
        d.line([(6, 5), (9, 4)], fill=light)
    elif shape == "crystal":
        d.polygon([(8, 1), (12, 6), (8, 15), (4, 6)], fill=c, outline=dark)
        d.line([(8, 2), (6, 6)], fill=light)
    elif shape == "ingot":
        d.polygon([(2, 9), (5, 5), (14, 5), (11, 9)], fill=light, outline=dark)
        d.rectangle([2, 9, 11, 12], fill=c, outline=dark)
        d.polygon([(11, 9), (14, 5), (14, 8), (11, 12)], fill=dark)
    elif shape == "plate":
        d.rectangle([2, 3, 13, 12], fill=c, outline=dark)
        d.line([(3, 4), (8, 4)], fill=light)
    elif shape == "wire":
        for i in range(4):
            d.arc([2 + i, 2 + i, 13 - i, 13 - i], 20, 340, fill=c if i % 2 == 0 else dark)
    elif shape == "kit":
        d.rectangle([2, 4, 13, 11], fill=(60, 64, 72, 255), outline=(30, 30, 36, 255))
        d.line([(4, 6), (11, 6)], fill=c)
        d.line([(4, 9), (11, 9)], fill=c)
    elif shape == "medkit":
        d.rectangle([2, 3, 13, 13], fill=(235, 235, 235, 255), outline=(120, 120, 120, 255))
        d.rectangle([7, 5, 8, 11], fill=c)
        d.rectangle([5, 7, 10, 8], fill=c)
    elif shape == "fish":
        d.ellipse([2, 5, 11, 11], fill=c, outline=dark)
        d.polygon([(11, 8), (15, 5), (15, 11)], fill=dark)
        d.point((4, 7), fill=(20, 20, 20, 255))
    elif shape == "tool":
        d.rectangle([2, 6, 11, 10], fill=c, outline=dark)
        d.polygon([(11, 5), (14, 3), (14, 13), (11, 11)], fill=(220, 230, 240, 255), outline=dark)
    elif shape == "bottle":
        d.rectangle([6, 1, 9, 4], fill=(150, 120, 90, 255))
        d.ellipse([3, 4, 12, 14], fill=c, outline=dark)
        d.line([(5, 7), (6, 6)], fill=light)
    else:  # organic
        d.ellipse([2, 3, 13, 13], fill=c, outline=dark)
        d.ellipse([5, 5, 8, 8], fill=light)
    return img


def main():
    ids = [i[0] for i in ITEMS]
    props = {i[0]: (i[6] if len(i) > 6 else {}) for i in ITEMS}
    known = set(ids)
    for result, _, ingredients in RECIPES:
        assert result in known, result
        assert len(ingredients) <= 9, result
        for ing in ingredients:
            assert ing in known or ing.startswith("minecraft:"), ing

    # Fabric mod: assets
    assets = os.path.join(MC, "resources", "assets", NS)
    for item_id, _, _, _, colour, shape, *_ in ITEMS:
        write_json(os.path.join(assets, "items", item_id + ".json"),
                   {"model": {"type": "minecraft:model", "model": "%s:item/%s" % (NS, item_id)}})
        write_json(os.path.join(assets, "models", "item", item_id + ".json"),
                   {"parent": "minecraft:item/generated", "textures": {"layer0": "%s:item/%s" % (NS, item_id)}})
        path = os.path.join(assets, "textures", "item", item_id + ".png")
        os.makedirs(os.path.dirname(path), exist_ok=True)
        placeholder(colour, shape).save(path)
    write_json(os.path.join(assets, "items", "subnautica_item.json"),
               {"model": {"type": "minecraft:model", "model": "%s:item/subnautica_item" % NS}})
    write_json(os.path.join(assets, "models", "item", "subnautica_item.json"),
               {"parent": "minecraft:item/generated", "textures": {"layer0": "%s:item/subnautica_item" % NS}})
    placeholder((150, 160, 170), "chunk").save(os.path.join(assets, "textures", "item", "subnautica_item.png"))
    for lang, column in (("en_us", 2), ("es_es", 3)):
        names = {"itemGroup.%s.subnautica" % NS: "Subnautica",
                 "item.%s.subnautica_item" % NS: "Subnautica Item" if lang == "en_us" else "Objeto de Subnautica"}
        names.update(UI[lang])
        names.update({"item.%s.%s" % (NS, i[0]): i[column] for i in ITEMS})
        write_json(os.path.join(assets, "lang", lang + ".json"), names)

    # Fabric mod: recipes
    recipes = os.path.join(MC, "resources", "data", NS, "recipe")
    for result, count, ingredients in RECIPES:
        recipe = {
            "type": "minecraft:crafting_shapeless",
            "category": "misc",
            "ingredients": [ing if ":" in ing else "%s:%s" % (NS, ing) for ing in ingredients],
            "result": {"id": "%s:%s" % (NS, result)},
        }
        if count != 1:
            recipe["result"]["count"] = count
        write_json(os.path.join(recipes, result + ".json"), recipe)

    for result, count, ingredients in RECHARGE:
        write_json(os.path.join(recipes, result + "_recharge.json"), {
            "type": "minecraft:crafting_shapeless", "category": "equipment",
            "ingredients": ["%s:%s" % (NS, i) for i in ingredients], "result": {"id": "%s:%s" % (NS, result)}})
    for raw, cooked in COOKING:
        for kind, time in (("smelting", 200), ("smoking", 100), ("campfire_cooking", 600)):
            write_json(os.path.join(recipes, "%s_from_%s.json" % (cooked, kind)), {
                "type": "minecraft:" + kind, "category": "food", "cookingtime": time, "experience": 0.35,
                "ingredient": "%s:%s" % (NS, raw), "result": {"id": "%s:%s" % (NS, cooked)}})

    # Fabric mod: the "Subnautica" advancements tab. The root shows from the start; each blueprint is granted by the
    # mod when the diver knows it (SnBlueprints), so its criterion can't be met any other way.
    advancements = os.path.join(MC, "resources", "data", NS, "advancement", "subnautica")
    write_json(os.path.join(advancements, "root.json"), {
        "criteria": {"always": {"trigger": "minecraft:tick"}},
        "display": {"icon": {"id": "%s:scanner" % NS}, "title": {"translate": "advancements.subnauticraft.root.title"},
                    "description": {"translate": "advancements.subnauticraft.root.description"},
                    "background": "%s:gui/advancements/backgrounds/sea" % NS, "show_toast": False, "announce_to_chat": False},
        "requirements": [["always"]]})
    learned = []
    for result, _, _ in RECIPES:
        if not result.startswith("cured_") and result not in learned:
            learned.append(result)
    for item_id in ids:
        if props[item_id].get("icon"):
            learned.append(item_id)  # the vehicles
    for item_id in learned:
        vehicle = props[item_id].get("icon")
        write_json(os.path.join(advancements, item_id + ".json"), {
            "parent": "%s:subnautica/root" % NS,
            "criteria": {"learned": {"trigger": "minecraft:impossible"}},
            "display": {"icon": {"id": "%s:%s" % (NS, item_id)}, "title": {"translate": "item.%s.%s" % (NS, item_id)},
                        "description": {"translate": "advancements.subnauticraft.vehicle" if vehicle else "advancements.subnauticraft.blueprint"},
                        "frame": "goal" if vehicle else "task", "show_toast": False, "announce_to_chat": False},
            "requirements": [["learned"]]})
    # the tab's background: deep water, our own drawing
    sea = Image.new("RGBA", (16, 16))
    for y in range(16):
        for x in range(16):
            ripple = ((x * 3 + y * 5) % 7) * 2
            sea.putpixel((x, y), (8 + ripple, 40 + ripple + y, 70 + ripple + y * 2, 255))
    path = os.path.join(assets, "textures", "gui", "advancements", "backgrounds", "sea.png")
    os.makedirs(os.path.dirname(path), exist_ok=True)
    sea.save(path)

    # Fabric mod: the item list
    def properties(item_id):
        p = props[item_id]
        if "food" in p:
            return "new Item.Properties().food(food(%d, %sF))" % (p["food"][0], p["food"][1])
        if "charge" in p:
            return "new Item.Properties().durability(%d)" % p["charge"]
        if p.get("icon"):
            return "new Item.Properties().stacksTo(1)"
        if "heal" in p:
            return "new Item.Properties().stacksTo(16)"
        return "new Item.Properties()"
    def add(i):
        if "heal" in props[i]:
            return '\t\tadd("%s", p -> new SnHealItem(p, %sF), %s, true);' % (i, props[i]["heal"], properties(i))
        return '\t\tadd("%s", Item::new, %s, %s);' % (i, properties(i), "false" if props[i].get("icon") else "true")
    adds = "\n".join(add(i) for i in ids)
    names = ",\n\t\t".join('"%s"' % i for i in ids)
    write(os.path.join(MC, "java", "dev", "subnauticraft", "SnItems.java"), """package dev.subnauticraft;

import java.util.ArrayList;
import java.util.List;
import java.util.function.Function;
import net.minecraft.core.Registry;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.core.registries.Registries;
import net.minecraft.network.chat.Component;
import net.minecraft.resources.Identifier;
import net.minecraft.resources.ResourceKey;
import net.minecraft.world.food.FoodProperties;
import net.minecraft.world.item.CreativeModeTab;
import net.minecraft.world.item.Item;
import net.minecraft.world.item.ItemStack;
import net.fabricmc.fabric.api.creativetab.v1.FabricCreativeModeTab;

/**
 * Subnautica's materials, fish and tools as Minecraft items (generated by tools/gen_items.py: edit the table there).
 * Steve picks them up in Subnautica's world and crafts them with Subnautica's own recipes; their creative tab is
 * "Subnautica". Fish are food; the flashlight and the Seaglide run on a battery (their durability is its charge).
 */
public final class SnItems {
	public static final String[] NAMES = {
		%s
	};
	/** Recipes Subnautica never locks behind a blueprint (SnBlueprints): salvage into titanium. */
	public static final String[] ALWAYS = {%s};
	public static final List<Item> ITEMS = new ArrayList<>();
	/** The ones in the creative tab (not the vehicles, which are only pictures). */
	public static final List<Item> IN_TAB = new ArrayList<>();
	/**
	 * Any other Subnautica item (one without its own Minecraft item): its kind, name and picture travel with the stack
	 * (custom data, custom name, custom model data: the picture comes from the icon pack). Nothing Steve picks up is lost.
	 */
	public static Item GENERIC;

	private SnItems() {
	}

	static void register() {
%s
		GENERIC = add("subnautica_item", SnGenericItem::new, new Item.Properties(), false);

		CreativeModeTab tab = FabricCreativeModeTab.builder()
			.title(Component.translatable("itemGroup.subnauticraft.subnautica"))
			.icon(() -> new ItemStack(ITEMS.getFirst()))
			.displayItems((parameters, output) -> IN_TAB.forEach(output::accept))
			.build();
		Registry.register(BuiltInRegistries.CREATIVE_MODE_TAB, Identifier.fromNamespaceAndPath(SubnautiCraft.ID, "subnautica"), tab);
	}

	private static Item add(final String name, final Function<Item.Properties, Item> factory, final Item.Properties properties, final boolean inTab) {
		ResourceKey<Item> key = ResourceKey.create(Registries.ITEM, Identifier.fromNamespaceAndPath(SubnautiCraft.ID, name));
		Item item = Registry.register(BuiltInRegistries.ITEM, key, factory.apply(properties.setId(key)));
		ITEMS.add(item);
		if (inTab) {
			IN_TAB.add(item);
		}

		return item;
	}

	private static FoodProperties food(final int nutrition, final float saturation) {
		return new FoodProperties.Builder().nutrition(nutrition).saturationModifier(saturation).build();
	}
}
""" % (names, ", ".join('"%s"' % a for a in ALWAYS), adds))

    # BepInEx plugin: TechType -> item id, and the recipes to check against the game's
    rows = "\n".join('            { TechType.%s, "%s" },' % (tech, item_id) for item_id, tech, *_ in ITEMS)
    by_id = {i[0]: i[1] for i in ITEMS}

    def tech(ing):
        return "TechType.None" if ing.startswith("minecraft:") else "TechType." + by_id[ing]
    recipe_rows = "\n".join('            new Recipe(TechType.%s, %d, new[] { %s }),' % (by_id[r], n, ", ".join(tech(i) for i in ing))
                            for r, n, ing in RECIPES)
    write(os.path.join(ROOT, "subnautica", "src", "SnItemTable.cs"), """using System.Collections.Generic;

namespace SubnautiCraft
{
    /// <summary>Generated by tools/gen_items.py (edit the table there): Subnautica's materials that are Minecraft items too.</summary>
    internal static class SnItemTable
    {
        public static readonly Dictionary<TechType, string> Items = new Dictionary<TechType, string>
        {
%s
        };

        /// <summary>The Minecraft recipes (TechType.None = a Minecraft ingredient), checked against the game's own at runtime.</summary>
        public static readonly Recipe[] Recipes =
        {
%s
        };

        public readonly struct Recipe
        {
            public readonly TechType Result;
            public readonly int Count;
            public readonly TechType[] Ingredients;

            public Recipe(TechType result, int count, TechType[] ingredients)
            {
                Result = result;
                Count = count;
                Ingredients = ingredients;
            }
        }
    }
}
""" % (rows, recipe_rows))
    print("%d items, %d recipes" % (len(ITEMS), len(RECIPES)))


if __name__ == "__main__":
    main()
