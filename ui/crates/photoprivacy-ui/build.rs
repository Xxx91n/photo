//! 为 windows-gnu 目标嵌入应用清单（Common Controls v6 + DPI）。
//!
//! 不能复用 gpui 的 `windows-manifest` 特性：它在 Windows 宿主上用 MSVC 的 rc.exe 生成 .lib，
//! 目标却是 *-windows-gnu 时 MinGW 链接器读不了（本机宿主 rustc 是 windows-msvc）。
//! 这里直接调用 windres 生成 COFF 对象并交给链接器，宿主与目标的组合不影响结果。

use std::path::PathBuf;
use std::process::Command;

fn main() {
    println!("cargo:rerun-if-changed=build.rs");
    println!("cargo:rerun-if-changed=resources/app.rc");
    println!("cargo:rerun-if-changed=resources/app.manifest");
    println!("cargo:rerun-if-env-changed=WINDRES");

    if std::env::var("CARGO_CFG_TARGET_OS").as_deref() != Ok("windows") {
        return;
    }
    if std::env::var("CARGO_CFG_TARGET_ENV").as_deref() != Ok("gnu") {
        // MSVC 目标不嵌清单（gpui 依赖 TaskDialogIndirect，缺清单会在加载期失败）。
        // Design(#49): Windows 发布链固定走 windows-gnu 并在此嵌入清单；MSVC 目标不具备生产可用性。
        println!(
            "cargo:warning=未为 {target} 嵌入应用清单：gpui 依赖 comctl32 v6，缺清单的产物启动即失败",
            target = std::env::var("TARGET").unwrap_or_default()
        );
        return;
    }

    let arch = std::env::var("CARGO_CFG_TARGET_ARCH").unwrap_or_default();
    let windres_target = match arch.as_str() {
        "x86_64" => "pe-x86-64",
        "aarch64" => "pe-aarch64-little",
        _ => "pe-i386",
    };

    let resources = PathBuf::from("resources");
    let out_dir = PathBuf::from(std::env::var("OUT_DIR").expect("OUT_DIR 未设置"));
    let object = out_dir.join("app-resource.o");
    let windres = std::env::var("WINDRES").unwrap_or_else(|_| "windres".to_string());

    let status = Command::new(&windres)
        .args(["--input", "resources/app.rc"])
        .arg("--output")
        .arg(&object)
        .arg("--output-format=coff")
        .arg("--include-dir")
        .arg(&resources)
        .arg(format!("--target={windres_target}"))
        .status();

    match status {
        Ok(status) if status.success() => {
            println!("cargo:rustc-link-arg={}", object.display());
        }
        Ok(status) => panic!("windres 编译应用清单失败（{status}）；路径：{windres}"),
        Err(err) => panic!(
            "无法运行 windres（{err}）：windows-gnu 构建需要 MinGW windres 在 PATH 或 WINDRES 环境变量中"
        ),
    }
}
