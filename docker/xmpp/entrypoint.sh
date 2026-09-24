#!/bin/bash
set -e

mkdir -p /var/run/prosody /var/lib/prosody
chown -R prosody:prosody /var/run/prosody /var/lib/prosody

# Generate self-signed certificate if needed
if [ ! -f /var/lib/prosody/localhost.crt ]; then
    echo "[prosody-init] Generating self-signed certificate for localhost..."
    prosodyctl cert generate localhost
    chown prosody:prosody /var/lib/prosody/localhost.*
fi

# Seed users and rosters if needed
if [ ! -f /var/lib/prosody/localhost/accounts/usera.dat ]; then
    echo "[prosody-init] Seeding default accounts (usera, userb, userc)..."
    prosodyctl register usera localhost password
    prosodyctl register userb localhost password
    prosodyctl register userc localhost password

    echo "[prosody-init] Setting up mutual roster contacts..."
    mkdir -p /var/lib/prosody/localhost/roster
    cat << 'EOF' > /var/lib/prosody/localhost/roster/usera.dat
return {
	["userb@localhost"] = {
		["subscription"] = "both";
		["groups"] = {
			["Contacts"] = true;
		};
		["name"] = "User B";
	};
	["userc@localhost"] = {
		["subscription"] = "both";
		["groups"] = {
			["Contacts"] = true;
		};
		["name"] = "User C";
	};
	[false] = {
		["version"] = 1;
	};
};
EOF

    cat << 'EOF' > /var/lib/prosody/localhost/roster/userb.dat
return {
	["usera@localhost"] = {
		["subscription"] = "both";
		["groups"] = {
			["Contacts"] = true;
		};
		["name"] = "User A";
	};
	["userc@localhost"] = {
		["subscription"] = "both";
		["groups"] = {
			["Contacts"] = true;
		};
		["name"] = "User C";
	};
	[false] = {
		["version"] = 1;
	};
};
EOF

    cat << 'EOF' > /var/lib/prosody/localhost/roster/userc.dat
return {
	["usera@localhost"] = {
		["subscription"] = "both";
		["groups"] = {
			["Contacts"] = true;
		};
		["name"] = "User A";
	};
	["userb@localhost"] = {
		["subscription"] = "both";
		["groups"] = {
			["Contacts"] = true;
		};
		["name"] = "User B";
	};
	[false] = {
		["version"] = 1;
	};
};
EOF

    chown -R prosody:prosody /var/lib/prosody/localhost

    # Run sample conversation seeding after Prosody binds to port 5222
    (
        sleep 2
        python3 /usr/local/bin/seed-messages.py
    ) &
fi

echo "[prosody-init] Starting Prosody server..."
exec su -s /bin/bash prosody -c "prosody -F"
