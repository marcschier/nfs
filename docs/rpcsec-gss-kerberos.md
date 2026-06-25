# RPCSEC_GSS and Kerberos

The RPC layer implements RFC 2203 RPCSEC_GSS wire framing with a pluggable GSS mechanism abstraction (`IGssMechanism`) and a loopback mechanism that exercises the `none`, `integrity`, and `privacy` services end to end.

The `KerberosGssMechanism` type is intentionally gated. Linux uses `libgssapi_krb5` and Windows uses SSPI (`secur32.dll`) for the native token exchange, but real verification needs a KDC, service principal, and keytab/domain credentials. Integration testing against a real realm (for example a WSL MIT Kerberos realm with a local KDC and a generated NFS service keytab) is therefore run out of band rather than in the default test suite.
