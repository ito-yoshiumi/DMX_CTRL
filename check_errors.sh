#!/bin/bash
# Unityログファイルからエラーメッセージを抽出するスクリプト

LOG_DIR="$HOME/Library/Application Support/DefaultCompany/DMX_CTRL/Logs"

if [ ! -d "$LOG_DIR" ]; then
    echo "ログディレクトリが見つかりません: $LOG_DIR"
    exit 1
fi

# 最新のログファイルを取得
LATEST_LOG=$(ls -t "$LOG_DIR"/unity_log_*.txt 2>/dev/null | head -1)

if [ -z "$LATEST_LOG" ]; then
    echo "ログファイルが見つかりません"
    exit 1
fi

echo "最新のログファイル: $LATEST_LOG"
if [ -f "$LATEST_LOG" ]; then
    echo "更新時刻: $(stat -f "%Sm" -t "%Y-%m-%d %H:%M:%S" "$LATEST_LOG" 2>/dev/null || stat -c "%y" "$LATEST_LOG" 2>/dev/null || echo "不明")"
    echo "ファイルサイズ: $(du -h "$LATEST_LOG" | cut -f1)"
fi
echo "=========================================="
echo ""

# エラーと例外を検索
echo "=== ERRORS ==="
ERRORS=$(grep -iE "\[ERROR\]|NullReference|MissingReference|ArgumentException|InvalidOperation" "$LATEST_LOG" 2>/dev/null)
if [ -z "$ERRORS" ]; then
    echo "エラーは見つかりませんでした"
else
    echo "$ERRORS" | tail -30
fi

echo ""
echo "=== EXCEPTIONS ==="
EXCEPTIONS=$(grep -iE "\[EXCEPTION\]|Exception:" "$LATEST_LOG" 2>/dev/null)
if [ -z "$EXCEPTIONS" ]; then
    echo "例外は見つかりませんでした"
else
    echo "$EXCEPTIONS" | tail -30
fi

echo ""
echo "=== WARNINGS (最新20件) ==="
WARNINGS=$(grep -i "\[WARNING\]" "$LATEST_LOG" 2>/dev/null)
if [ -z "$WARNINGS" ]; then
    echo "警告は見つかりませんでした"
else
    echo "$WARNINGS" | tail -20
fi

echo ""
echo "=== コンパイルエラー (CSエラー) ==="
COMPILE_ERRORS=$(grep -iE "error CS[0-9]+|does not contain|could not find|is not defined" "$LATEST_LOG" 2>/dev/null)
if [ -z "$COMPILE_ERRORS" ]; then
    echo "コンパイルエラーは見つかりませんでした"
else
    echo "$COMPILE_ERRORS" | tail -30
fi

echo ""
echo "=== ログファイルの最後の100行 ==="
tail -100 "$LATEST_LOG"

