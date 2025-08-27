#!/bin/bash

echo "TheAuxilia Report Service Test"
echo "=============================="
echo ""
echo "This will execute the report immediately and send to:"
echo "- jlee@theauxilia.com"
echo "- jjacobsen@theauxilia.com"
echo ""
read -p "Continue? (y/n) " -n 1 -r
echo ""

if [[ $REPLY =~ ^[Yy]$ ]]; then
    echo "Running report generation..."
    dotnet run -- --now
else
    echo "Test cancelled"
fi