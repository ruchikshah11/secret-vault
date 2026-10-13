namespace SecretVault.Crypto;

// Short, common, easy-to-type English words for passphrases. Duplicates are removed at load time;
// with ~700 words each word adds about 9.5 bits of entropy.
internal static class Wordlist
{
    public static readonly IReadOnlyList<string> Words = Raw
        .Split((char[])[' ', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries)
        .Distinct()
        .ToArray();

    private const string Raw = """
        able acid acorn acre actor adapt admit adult agent agree ahead aide aisle alarm album alert alley allow
        alpha amber amend ample angel anger angle ankle apple april apron arena argue armor army aroma arrow
        artist ashes aspen atlas atom attic audio avoid awake award axis bacon badge bagel baker balmy bamboo
        banana banjo barn baron basil basin basket batch bath beach beacon bead beam bean bear beard beaver
        bench berry bike birch bird bison blade blanket blaze blend blimp bloom blossom blue blush board boat
        bonus book boost booth boots bottle boulder bounce bowl brain branch brave bread breeze brick bridge
        brief bright brisk broom brook brush bubble bucket buddy budget buffalo bugle build bulb bundle bunny
        burst butter button cabin cable cactus cake calm camel camera camp canal candle candy canoe canvas
        canyon cape captain caramel carbon cargo carpet carrot cart castle cedar cello chalk champ chant chapel
        charm chart cheese cherry chess chief chimney chip chorus cider cinema circle citrus civic clam clay
        clever cliff climb clock cloud clover coast cobalt cocoa coconut comet comic coral corner cotton couch
        cougar cove coyote crab cradle craft crane crayon creek crest cricket crisp crown cruise crystal cube
        cupcake curly curve cushion cycle daisy dance dawn deck delta denim desert desk detail dial diamond
        diary dice diesel dinner dipper disco dock dolphin domain donut dove dragon drama dream drift drill
        drum duck dune dusk dwarf eagle earth easel echo eclipse edge elbow elder elephant elm ember emerald
        empire engine enjoy epic equal error essay ether event exact exit fable fabric falcon fancy farm
        feast feather fence ferry fiber field fiesta finch fire fjord flag flame flash fleet flint flock
        flora flute focus foggy forest fork fossil fountain fox frame fresh frost fruit fudge galaxy garden
        garlic gazelle gecko gem genius giant ginger giraffe glacier glade glass globe glove glow goat gold
        golf goose gopher gospel grain granite grape graph grass gravel gravy green grid grill grove guitar
        gulf habit hammer hamster harbor harp harvest hawk hazel heart hedge helmet hero heron hickory hill
        hippo hobby honey hood hope horizon horse hotel humble hunter husky igloo image index indigo inlet
        input iris iron island ivory jacket jade jaguar jam jar jasmine jazz jeans jelly jewel jigsaw jolly
        journal judge juice jumbo jungle junior kayak kernel kettle kingdom kite kiwi knight koala label
        ladder lagoon lake lamp lantern laptop lark laser latte lava lawn leaf lemon lens leopard letter level
        lilac lily lime linen lion lizard llama lobster locket lodge logic lotus lucky lunar lunch lyric
        magnet mango mantle maple marble march marsh mask meadow medal melody melon memo mercury mesa metal
        meteor method mint mirror mist mitten mocha model monkey moose morning mosaic moss motor mountain
        muffin mural museum music nacho napkin native nature nebula nectar needle nest nickel night noble
        noodle north novel nugget nutmeg oak oasis ocean octave olive omega onion opal opera orbit orchid
        organ otter outfit owl oxygen oyster paddle palace palm panda panel paper parade parrot pasta pastel
        patio peach peanut pearl pebble pecan pelican pencil pepper piano pickle pigeon pillow pilot pine
        pirate pixel pizza planet plaza plum poem polar pond poppy porch potato prairie prism pulse pumpkin
        puppet puzzle python quail quartz quest quilt quiver rabbit raccoon radar radio rain ranch raven
        razor recipe reef relay remedy rhino ribbon ridge ring ripple river road robin robot rocket rodeo
        rose ruby rugby ruler saddle safari saga salad salmon salsa sand satin saturn scarf scenic school
        scout sea seal season seed shadow shark shell sherpa shield ship shore shrimp sierra silk silver siren
        skate sketch sky slate sled slope smile snack snail snow soap socket sofa solar sonnet spark sparrow
        spice spider spiral splash sponge spoon spring sprout squash squid stable stamp star statue steam
        stone storm story stove straw stream studio sugar summit sunny surf swan sweater swift symbol syrup
        table taco talent tango tank tapir teapot tempo tennis tent thistle thunder ticket tiger timber toast
        token tomato topaz torch tortoise totem towel tower trail train tree trophy tropic truck trumpet tulip
        tundra tunnel turtle tuxedo twig twin umbrella unicorn union urban valley vanilla vapor velvet venus
        verse violet violin viper visor vista vivid vortex voyage waffle wagon walnut walrus wander water wave
        wealth weasel wheat whale whistle willow window winter wisdom wizard wolf wombat wonder wood wool
        world yacht yard yarn yellow yeti yoga yogurt zebra zenith zephyr zero zigzag zinc zipper zone
        """;
}
