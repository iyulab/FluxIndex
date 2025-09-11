#!/usr/bin/env python3
"""
Convert Cross-Encoder models to ONNX format for use with FluxIndex
Supports models from Hugging Face model hub
"""

import os
import sys
import argparse
import torch
from transformers import AutoTokenizer, AutoModelForSequenceClassification
from torch.onnx import export as onnx_export

def convert_cross_encoder_to_onnx(
    model_name: str,
    output_path: str,
    max_length: int = 512,
    opset_version: int = 14
):
    """
    Convert a cross-encoder model to ONNX format
    
    Args:
        model_name: Hugging Face model name (e.g., 'cross-encoder/ms-marco-MiniLM-L6-v2')
        output_path: Path to save the ONNX model
        max_length: Maximum sequence length
        opset_version: ONNX opset version
    """
    print(f"Loading model: {model_name}")
    
    # Load model and tokenizer
    tokenizer = AutoTokenizer.from_pretrained(model_name)
    model = AutoModelForSequenceClassification.from_pretrained(model_name)
    model.eval()
    
    # Create dummy input
    dummy_query = "What is machine learning?"
    dummy_doc = "Machine learning is a subset of artificial intelligence."
    
    inputs = tokenizer(
        dummy_query,
        dummy_doc,
        padding='max_length',
        truncation=True,
        max_length=max_length,
        return_tensors='pt'
    )
    
    # Export to ONNX
    print(f"Converting to ONNX format...")
    
    with torch.no_grad():
        onnx_export(
            model,
            (inputs['input_ids'], inputs['attention_mask'], inputs['token_type_ids']),
            output_path,
            export_params=True,
            opset_version=opset_version,
            input_names=['input_ids', 'attention_mask', 'token_type_ids'],
            output_names=['logits'],
            dynamic_axes={
                'input_ids': {0: 'batch_size', 1: 'sequence'},
                'attention_mask': {0: 'batch_size', 1: 'sequence'},
                'token_type_ids': {0: 'batch_size', 1: 'sequence'},
                'logits': {0: 'batch_size'}
            }
        )
    
    print(f"✅ Model saved to: {output_path}")
    print(f"   Model dimensions: batch_size x {max_length}")
    print(f"   Output shape: batch_size x {model.config.num_labels}")
    
    # Verify the exported model
    try:
        import onnxruntime as ort
        
        print("\nVerifying ONNX model...")
        session = ort.InferenceSession(output_path)
        
        # Test inference
        outputs = session.run(
            None,
            {
                'input_ids': inputs['input_ids'].numpy(),
                'attention_mask': inputs['attention_mask'].numpy(),
                'token_type_ids': inputs['token_type_ids'].numpy()
            }
        )
        
        print(f"✅ Verification successful!")
        print(f"   Output shape: {outputs[0].shape}")
        
    except ImportError:
        print("⚠️ onnxruntime not installed. Skipping verification.")
        print("   Install with: pip install onnxruntime")

def download_and_convert_popular_models(output_dir: str):
    """
    Download and convert popular cross-encoder models
    """
    os.makedirs(output_dir, exist_ok=True)
    
    models = [
        ('cross-encoder/ms-marco-MiniLM-L6-v2', 'ms-marco-MiniLM-L6-v2.onnx'),
        ('cross-encoder/ms-marco-MiniLM-L12-v2', 'ms-marco-MiniLM-L12-v2.onnx'),
        ('cross-encoder/ms-marco-TinyBERT-L-2-v2', 'ms-marco-TinyBERT-L2-v2.onnx'),
    ]
    
    for model_name, output_name in models:
        output_path = os.path.join(output_dir, output_name)
        
        if os.path.exists(output_path):
            print(f"⏭️ {output_name} already exists, skipping...")
            continue
        
        try:
            convert_cross_encoder_to_onnx(model_name, output_path)
        except Exception as e:
            print(f"❌ Failed to convert {model_name}: {e}")

def main():
    parser = argparse.ArgumentParser(
        description='Convert Cross-Encoder models to ONNX format'
    )
    
    parser.add_argument(
        '--model',
        type=str,
        default='cross-encoder/ms-marco-MiniLM-L6-v2',
        help='Hugging Face model name or path'
    )
    
    parser.add_argument(
        '--output',
        type=str,
        default='models/cross-encoder.onnx',
        help='Output path for ONNX model'
    )
    
    parser.add_argument(
        '--max-length',
        type=int,
        default=512,
        help='Maximum sequence length'
    )
    
    parser.add_argument(
        '--download-popular',
        action='store_true',
        help='Download and convert popular models'
    )
    
    parser.add_argument(
        '--output-dir',
        type=str,
        default='models',
        help='Output directory for models'
    )
    
    args = parser.parse_args()
    
    if args.download_popular:
        download_and_convert_popular_models(args.output_dir)
    else:
        # Ensure output directory exists
        os.makedirs(os.path.dirname(args.output), exist_ok=True)
        convert_cross_encoder_to_onnx(
            args.model,
            args.output,
            args.max_length
        )

if __name__ == '__main__':
    # Check dependencies
    try:
        import transformers
        import torch
    except ImportError as e:
        print("❌ Missing dependencies!")
        print("Install with: pip install transformers torch")
        sys.exit(1)
    
    main()