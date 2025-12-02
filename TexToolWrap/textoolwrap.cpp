//for crunch lol
#if defined(_WIN32)
#define WIN32
#endif

#include "crunch/inc/crnlib.h"
#include "crunch/inc/crn_decomp.h"
#include <cstring>
#include <stdio.h>
#include <map>
#include <algorithm>
// For CoTaskMemAlloc
#include <Objbase.h>

#if defined(_MSC_VER)
	#define EXPORT extern "C" __declspec(dllexport)
#elif defined(__GNUC__)
	#define EXPORT extern "C" __attribute__((visibility("default")))
#endif

// todo: we need to use two different versions of crunch: the original and the unity fork.
// currently we just use the unity fork. need to look into when and where to use the original one.
EXPORT unsigned char* EncodeByCrunchUnity(unsigned int& returnLength, void* data, int mode, int level, unsigned int width, unsigned int height, unsigned int ver, int mips) {
	crn_comp_params comp_params;
	comp_params.m_width = width;
	comp_params.m_height = height;
	comp_params.set_flag(cCRNCompFlagPerceptual, true);
	//comp_params.set_flag(cCRNCompFlagDXT1AForTransparency, false); //unsure if unity dxt1 is ever transparent?
	comp_params.set_flag(cCRNCompFlagHierarchical, true);
	comp_params.m_file_type = cCRNFileTypeCRN;

	switch (mode) {
		case 28:
			comp_params.m_format = cCRNFmtDXT1;
			break;
		case 29:
			comp_params.m_format = cCRNFmtDXT5;
			break;
		default:
			return 0;
	}

	comp_params.m_pImages[0][0] = (crn_uint32*)data;
	comp_params.m_quality_level = 128; //cDefaultCRNQualityLevel

	comp_params.m_userdata0 = ver; //custom version field??? idek
	comp_params.m_num_helper_threads = 1; //staying safe for xplat for now

	crn_mipmap_params mip_params;
	mip_params.m_gamma_filtering = true;

	// probably causes mass chaos if we go over since
	// the asset field wouldn't've been set but w/e
	// hope that doesn't happen here :shrugs:
	mips = std::clamp(mips, 1, (int)cCRNMaxLevels);

	if (mips == 1) {
		mip_params.m_mode = cCRNMipModeNoMips;
	} else {
		mip_params.m_mode = cCRNMipModeGenerateMips;
	}

	mip_params.m_max_levels = mips;

	crn_uint32 actual_quality_level;
	float actual_bitrate;
	crn_uint32 output_file_size;

	unsigned char* unsafeData = static_cast<unsigned char*>(crn_compress(comp_params, mip_params, output_file_size, &actual_quality_level, &actual_bitrate));

	if (unsafeData == nullptr)
		return nullptr;

	if (output_file_size == 0) {
		delete[] unsafeData;
		return nullptr;
	}

	unsigned char* newData = static_cast<unsigned char*>(CoTaskMemAlloc(output_file_size));

	// Check if the allocation failed
	if (newData == nullptr) {
		delete[] unsafeData;
		return nullptr;
	}

	memcpy(newData, unsafeData, output_file_size);
	delete[] unsafeData;

	// if the length is 0, return nullptr
	returnLength = output_file_size;

	return newData;
}

EXPORT unsigned char* DecodeByCrunchUnity(unsigned int& returnLength, void* data, unsigned int byteSize) {
	crnd::crn_texture_info tex_info;
	tex_info.m_struct_size = sizeof(crnd::crn_texture_info);
	if (!crnd_get_texture_info(data, byteSize, &tex_info)) {
		returnLength = 0;
		return nullptr;
	}

	crnd::crnd_unpack_context context = crnd::crnd_unpack_begin(data, byteSize);
	if (!context) {
		returnLength = 0;
		return nullptr;
	}

	const crnd::uint level_width = crnd::math::maximum<crnd::uint>(1U, tex_info.m_width);
	const crnd::uint level_height = crnd::math::maximum<crnd::uint>(1U, tex_info.m_height);
	const crnd::uint num_blocks_x = (level_width + 3U) >> 2U;
	const crnd::uint num_blocks_y = (level_height + 3U) >> 2U;
	const crnd::uint row_pitch = num_blocks_x * tex_info.m_bytes_per_block;
	const crnd::uint size_of_face = num_blocks_y * row_pitch;

	unsigned char* decodedData = static_cast<unsigned char*>(CoTaskMemAlloc(size_of_face));
	if (decodedData == nullptr) {
		crnd::crnd_unpack_end(context);
		returnLength = 0;
		return nullptr;
	}

	void* destination = decodedData;
	bool success = crnd::crnd_unpack_level(context, &destination, size_of_face, row_pitch, 0);
	crnd::crnd_unpack_end(context);

	if (!success) {
		CoTaskMemFree(decodedData);
		returnLength = 0;
		return nullptr;
	}

	returnLength = size_of_face;
	return decodedData;
}