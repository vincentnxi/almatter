//! Signs an Almatter installer for the app's automatic updates (see
//! almatter-core's updates.rs). Not run by hand: tools/sign-update.ps1 calls
//! it, handing over the secret key on standard input so that it never
//! appears on a command line, in a file in the clear, or on screen.
//!
//!   sign_update public-key
//!       prints the public key matching the secret key on stdin.
//!   sign_update sign <path\to\Almatter-Setup-X.Y.Z.exe> <X.Y.Z>
//!       writes <installer>.sig next to it, after checking that the app's
//!       own keys accept it.

use std::io::Read;
use std::path::Path;
use std::process::ExitCode;

use almatter_core::updates::{
    decode_hex, encode_hex, parse_signature, sha256_file, shipped_update_keys, signed_message, verify_installer_signature,
};
use ed25519_dalek::{Signer, SigningKey};

fn main() -> ExitCode {
    match run() {
        Ok(()) => ExitCode::SUCCESS,
        Err(message) => {
            eprintln!("sign_update: {message}");
            ExitCode::FAILURE
        }
    }
}

fn run() -> Result<(), String> {
    let args: Vec<String> = std::env::args().skip(1).collect();
    let key = read_secret_key()?;
    match args.iter().map(String::as_str).collect::<Vec<_>>().as_slice() {
        ["public-key"] => {
            println!("{}", encode_hex(key.verifying_key().as_bytes()));
            Ok(())
        }
        ["sign", installer, version] => sign(&key, Path::new(installer), version),
        _ => Err("usage: sign_update public-key | sign_update sign <installer> <version>  (secret key on stdin)".into()),
    }
}

fn read_secret_key() -> Result<SigningKey, String> {
    let mut text = String::new();
    std::io::stdin().read_to_string(&mut text).map_err(|e| format!("cannot read the key: {e}"))?;
    // PowerShell may put a byte-order mark in front of what it pipes.
    let text = text.trim_matches(|c: char| c.is_whitespace() || c == '\u{feff}');
    let seed = decode_hex::<32>(text).ok_or("standard input holds no secret key (64 hex digits)")?;
    Ok(SigningKey::from_bytes(&seed))
}

fn sign(key: &SigningKey, installer: &Path, version: &str) -> Result<(), String> {
    // The file must be the one for this version: signing 0.18.0's name onto
    // another installer is exactly the mistake the version in the message
    // exists to catch, so catch it here first.
    let expected = format!("Almatter-Setup-{version}.exe");
    if installer.file_name().and_then(|n| n.to_str()) != Some(expected.as_str()) {
        return Err(format!("expected a file named {expected}, got {}", installer.display()));
    }
    let digest = sha256_file(installer)?;
    let signature = key.sign(&signed_message(version, &digest));
    let text = encode_hex(&signature.to_bytes()) + "\n";

    // Read back through the app's own code, against the keys the app ships:
    // a signature the app would refuse is not written at all.
    let keys = shipped_update_keys()?;
    verify_installer_signature(version, &digest, &parse_signature(text.as_bytes())?, &keys)
        .map_err(|e| format!("{e} — is this the key the app was built with?"))?;

    let sig_path = installer.with_file_name(format!("{expected}.sig"));
    std::fs::write(&sig_path, text).map_err(|e| format!("cannot write {}: {e}", sig_path.display()))?;
    println!("sha256 {}", encode_hex(&digest));
    println!("signed {}", sig_path.display());
    Ok(())
}
