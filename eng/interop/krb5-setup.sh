#!/usr/bin/env bash
# Provisions a local MIT Kerberos realm for the gated RPCSEC_GSS integration test.
set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
REPO="$(cd "$HERE/../.." && pwd)"
WORK_DIR="${KRB5_WORK_DIR:-$REPO/eng/interop/.krb5-work}"
REALM="${KRB5_REALM:-NFS.TEST}"
CLIENT_PRINCIPAL="${KRB5_CLIENT_PRINCIPAL:-testuser}"
CLIENT_PASSWORD="${KRB5_CLIENT_PASSWORD:-testpassword}"
SERVICE_PRINCIPAL="${KRB5_SERVICE_PRINCIPAL:-nfs/localhost}"
HOST_PRINCIPAL="${KRB5_HOST_PRINCIPAL:-host/localhost}"
KEYTAB="$WORK_DIR/nfs.keytab"
CCACHE="$WORK_DIR/testuser.ccache"
KEYTAB_NAME="FILE:$KEYTAB"
CCACHE_NAME="FILE:$CCACHE"

mkdir -p "$WORK_DIR"
chmod 700 "$WORK_DIR"

sudo mkdir -p /etc/krb5kdc
sudo tee /etc/krb5.conf >/dev/null <<EOF
[libdefaults]
    default_realm = $REALM
    dns_lookup_kdc = false
    dns_lookup_realm = false
    rdns = false
    ticket_lifetime = 24h
    forwardable = true

[realms]
    $REALM = {
        kdc = localhost
        admin_server = localhost
    }

[domain_realm]
    .localhost = $REALM
    localhost = $REALM
EOF

sudo tee /etc/krb5kdc/kdc.conf >/dev/null <<EOF
[kdcdefaults]
    kdc_ports = 88
    kdc_tcp_ports = 88

[realms]
    $REALM = {
        database_name = /var/lib/krb5kdc/principal
        admin_keytab = FILE:/etc/krb5kdc/kadm5.keytab
        acl_file = /etc/krb5kdc/kadm5.acl
        key_stash_file = /etc/krb5kdc/stash
        max_life = 24h
        max_renewable_life = 7d
        supported_enctypes = aes256-cts-hmac-sha1-96:normal aes128-cts-hmac-sha1-96:normal
    }
EOF

echo "*/admin@$REALM *" | sudo tee /etc/krb5kdc/kadm5.acl >/dev/null
sudo rm -f /var/lib/krb5kdc/principal /var/lib/krb5kdc/principal.kadm5 \
    /var/lib/krb5kdc/principal.kadm5.lock /var/lib/krb5kdc/principal.ok /etc/krb5kdc/stash
sudo kdb5_util create -s -r "$REALM" -P masterpassword
sudo kadmin.local -q "addprinc -pw $CLIENT_PASSWORD $CLIENT_PRINCIPAL"
sudo kadmin.local -q "addprinc -randkey $SERVICE_PRINCIPAL"
sudo kadmin.local -q "addprinc -randkey $HOST_PRINCIPAL"
sudo kadmin.local -q "ktadd -k $KEYTAB $SERVICE_PRINCIPAL $HOST_PRINCIPAL"
sudo chown "$(id -u):$(id -g)" "$KEYTAB"
chmod 600 "$KEYTAB"

sudo service krb5-kdc restart || sudo /usr/sbin/krb5kdc
sudo service krb5-admin-server restart || sudo /usr/sbin/kadmind

for attempt in 1 2 3 4 5 6 7 8 9 10; do
    if printf '%s\n' "$CLIENT_PASSWORD" | KRB5CCNAME="$CCACHE_NAME" kinit "$CLIENT_PRINCIPAL@$REALM"; then
        break
    fi

    if [ "$attempt" = 10 ]; then
        echo "kinit failed after waiting for the local KDC" >&2
        exit 1
    fi

    sleep 1
done

if [ -n "${GITHUB_ENV:-}" ]; then
    {
        echo "KRB5_CONFIG=/etc/krb5.conf"
        echo "KRB5CCNAME=$CCACHE_NAME"
        echo "KRB5_KTNAME=$KEYTAB_NAME"
        echo "NFS_KRB5_TEST=1"
        echo "NFS_KRB5_SERVICE_NAME=nfs@localhost"
    } >> "$GITHUB_ENV"
fi

cat <<EOF
Kerberos test realm ready.
  realm: $REALM
  client: $CLIENT_PRINCIPAL@$REALM
  service: $SERVICE_PRINCIPAL@$REALM
  keytab: $KEYTAB
  ccache: $CCACHE

Export these variables to run the gated tests locally:
  export KRB5_CONFIG=/etc/krb5.conf
  export KRB5CCNAME=$CCACHE_NAME
  export KRB5_KTNAME=$KEYTAB_NAME
  export NFS_KRB5_TEST=1
  export NFS_KRB5_SERVICE_NAME=nfs@localhost
EOF
