#!/bin/bash

# Test script for bill synchronization
# Usage: ./test_bill_sync.sh

# Configuration
FUNCTION_URL="http://localhost:7071/api/SyncBillsToVom"
TOKEN="POS-KARAGE638944676684535831YYhG"

echo "Testing bill synchronization..."
echo "Token: $TOKEN"
echo "URL: $FUNCTION_URL"
echo ""

# Make the API call
curl -X POST "$FUNCTION_URL" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -v \
  | jq '.' 2>/dev/null || cat

echo ""
echo "Test completed."