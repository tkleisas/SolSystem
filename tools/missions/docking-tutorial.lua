-- The first mission: put the courier down Meridian's corridor under its own power.
--
-- Guidance, not law: the latches decide. This script reads the same numbers the probe
-- prints — range, lateral offset, closing speed — and talks when a number says something
-- a pilot could act on. The verdict at the bottom is the docking law's, not ours.

local said_slow = false
local said_align = false
local said_creep = false
local said_latched = false

mission.outcome("docked", function() return sim.in_contact() end)
mission.outcome("scraped_the_port", function()
    return (not sim.in_contact()) and sim.range() < 1.5 and math.abs(sim.closing()) > 1.2 end)

function mission.on_started()
    mission.say("welcome aboard the courier. Meridian's dock is four hundred metres down the corridor, and the two of you are holding formation.")
    mission.note("bring her in nose-first: closing under two metres a second, lateral drift near zero, then let the latches do the rest.")
end

function mission.on_sim(dt)
    local range = sim.range()
    local closing = sim.closing()
    local lateral = sim.lateral()

    -- Over a fourth of the approach at more than four metres a second: the brake onto the
    -- profile will burn fuel the rest of the flight will want.
    if not said_slow and range < 1000.0 and closing > 4.0 then
        said_slow = true
        mission.say("check your closing rate: it is high. ease the throttle — the brake through the last kilometre costs fuel you will want at the hatch.")
    end

    -- Off the centreline on the way in: recapture now, while the port is still across the
    -- view, not under it.
    if not said_align and range < 400.0 and lateral > 6.0 then
        said_align = true
        mission.note(string.format("lateral offset %.1f m — recapture the centreline before closing further.", lateral))
    end

    -- Down to the last hundred metres in one line: creep, blips only, and the latches
    -- handle the last of it inside two metres.
    if not said_creep and range < 120.0 and lateral < 3.0 then
        said_creep = true
        mission.say("the corridor looks good. from here it is throttle blips — under half a metre a second at the hatch — and the latches take the last two metres.")
    end

    if not said_latched and sim.docked() and not sim.in_contact() then
        said_latched = true
        mission.note("inside the latches' grasp: hold her steady and bridge the last half-metre.")
    end
end
