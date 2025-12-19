#!/bin/bash
set -e

echo "Installing OCR and Imaging dependencies..."
sudo apt-get update
sudo apt-get install -y libleptonica-dev libtesseract-dev libgdiplus libc6-dev

echo "Dependencies installed. You can now try running the tests or application again."
