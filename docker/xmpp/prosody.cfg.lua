-- Prosody Configuration for Stanza Local Development Environment
-- Domain: localhost

admins = { "usera@localhost" }

-- Plugin paths
plugin_paths = { "/usr/lib/prosody-modules" }

-- Enabled modules for localhost
modules_enabled = {
    -- Core & Network
    "roster";
    "saslauth";
    "tls";
    "disco";

    -- Modern XEPs
    "carbons";       -- XEP-0280: Message Carbons
    "pep";           -- XEP-0060 / XEP-0163: Personal Eventing Protocol (OMEMO, Avatars)
    "private";       -- XEP-0049: Private XML Storage
    "blocklist";     -- XEP-0191: Blocking Command
    "vcard4";        -- XEP-0292: vCard4 Over XMPP
    "vcard_legacy";  -- Conversion between vCard and PEP Avatar

    -- Archive & Sync
    "mam";           -- XEP-0313: Message Archive Management
    "offline";       -- Offline message storage
    "csi_simple";    -- XEP-0352: Client State Indication

    -- Server Info & Diagnostics
    "version";
    "uptime";
    "time";
    "ping";
    "admin_adhoc";
    "register";      -- XEP-0077: In-Band Registration
};

modules_disabled = {
    "s2s";           -- Disable server-to-server federation for local isolation
};

-- Allow in-band registration for testing additional users
allow_registration = true

-- Security & Encryption settings for local dev
c2s_require_encryption = false
s2s_require_encryption = false
allow_unencrypted_plain_auth = true

-- Network ports
c2s_ports = { 5222 }
c2s_direct_tls_ports = { 5223 }
http_ports = { 5280 }
http_interfaces = { "*" }

-- Data and PID storage
pidfile = "/var/run/prosody/prosody.pid"
authentication = "internal_hashed"
storage = "internal"

-- Keep MAM message archives indefinitely for evaluation
archive_expires_after = "never"
default_archive_policy = true

-- Log output directly to stdout for docker logs
log = {
    { levels = { min = "info" }, to = "console" };
}

-- Certificates location
certificates = "/var/lib/prosody"

----------- Virtual Hosts -----------

VirtualHost "localhost"
    certificate = "/var/lib/prosody/localhost.crt"
    key = "/var/lib/prosody/localhost.key"

----------- Components -----------

Component "conference.localhost" "muc"
    name = "Local Chatrooms"
    restrict_room_creation = false
    modules_enabled = { "muc_mam" }
    muc_room_default_public = true
    muc_room_default_persistent = true

Component "upload.localhost" "http_upload"
    http_upload_file_size_limit = 104857600 -- 100 MB
    http_upload_quota = 1073741824          -- 1 GB
